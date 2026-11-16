// The scenarios of cases/03-xamarin-forms-to-maui/scenarios/Scenarios.cs, ported one for one.
// There they drive view models. Here they drive the rendered screens: press, type, read what is on screen.
// Events are awaited: in this version of the testing library they are asynchronous, and a press fired before a
// preceding text change has been applied sees the old state.
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import { http, startApp, TestApp } from '../test-support/harness';

let app: TestApp;
afterEach(async () => {
  await app?.stop();
});

const hidden = { includeHiddenElements: true };
const rows = () => screen.queryAllByTestId(/^shipment-/, hidden).map((row) => row.props.testID.replace('shipment-', ''));
const listLoaded = () => waitFor(() => expect(rows().length).toBeGreaterThan(0));
const press = (label: string | RegExp) => fireEvent.press(screen.getByRole('button', { name: label }));

describe('shipment list', () => {
  test('list shows all shipments in tracking number order', async () => {
    app = await startApp();
    await listLoaded();

    expect(rows()).toEqual(['SHP-000001', 'SHP-000002', 'SHP-000003']);
    const second = within(screen.getByTestId('shipment-SHP-000002'));
    expect(second.getByText('SHP-000002 · Jean Martin')).toBeTruthy();
    expect(second.getByText('Lyon, FR · In transit')).toBeTruthy();
    expect(screen.queryByText('Offline: showing saved shipments')).toBeNull();
  });

  test('choosing a status filter reloads the list', async () => {
    app = await startApp();
    await listLoaded();

    await press('Filter In transit');

    await waitFor(() => expect(rows()).toEqual(['SHP-000002']));
  });

  test('offline without saved data shows a message and an empty list', async () => {
    app = await startApp({ online: false });

    await screen.findByText('You are offline. No saved shipments yet.');
    expect(screen.getByText('Offline: showing saved shipments')).toBeTruthy();
    expect(rows()).toEqual([]);
  });

  test('offline after a successful load shows the saved shipments', async () => {
    app = await startApp();
    await listLoaded();

    app.setOnline(false);
    await act(async () => screen.getByTestId('list').props.refreshControl.props.onRefresh());

    await screen.findByText('Offline: showing saved shipments');
    expect(rows()).toHaveLength(3);
    expect(screen.queryByText('You are offline. No saved shipments yet.')).toBeNull();
  });

  test('tapping a shipment opens its detail screen', async () => {
    app = await startApp();
    await listLoaded();

    await fireEvent.press(screen.getByTestId('shipment-SHP-000002'));

    await screen.findByText('Jean Martin');
    expect(screen.getByTestId('status')).toHaveTextContent('In transit');
    expect(screen.getByRole('button', { name: 'Mark delivered' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Mark in transit' })).toBeNull();
  });
});

describe('shipment detail', () => {
  test('marking as delivered updates the detail and the list behind it', async () => {
    app = await startApp();
    await listLoaded();
    await fireEvent.press(screen.getByTestId('shipment-SHP-000002'));
    await screen.findByText('Jean Martin');

    await press('Mark delivered');

    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('Delivered'));
    expect(screen.getByTestId('delivered')).toHaveTextContent('Delivered 2026-02-10 12:00 UTC');
    expect(screen.queryByRole('button', { name: 'Mark delivered' })).toBeNull();
    await waitFor(() => expect(within(screen.getByTestId('shipment-SHP-000002', hidden)).getByText('Lyon, FR · Delivered', hidden)).toBeTruthy());
  });

  test('a delivered shipment offers no actions', async () => {
    app = await startApp();
    await listLoaded();
    await fireEvent.press(screen.getByTestId('shipment-SHP-000003'));
    await screen.findByText('John Miller');

    expect(screen.getByTestId('delivered')).toHaveTextContent('Delivered 2026-02-07 15:45 UTC');
    expect(screen.queryByRole('button', { name: 'Mark in transit' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Mark delivered' })).toBeNull();
  });

  test('a change made elsewhere is reported and the screen shows the current state', async () => {
    app = await startApp();
    await listLoaded();
    await fireEvent.press(screen.getByTestId('shipment-SHP-000001'));
    await screen.findByRole('button', { name: 'Mark in transit' });

    // Another user moves the same shipment through to Delivered while this screen is open.
    for (const status of ['InTransit', 'Delivered']) {
      const response = await http(`${app.backendUrl}/api/shipments/SHP-000001/status`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ status }),
      });
      expect(response.ok).toBe(true);
    }

    await press('Mark in transit');

    await waitFor(() => expect(app.alerts).toEqual(['Cannot change status from Delivered to InTransit.']));
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('Delivered'));
  });
});

describe('new shipment', () => {
  const openForm = async () => {
    await listLoaded();
    await press('New');
    await screen.findByPlaceholderText('Recipient name');
  };
  const type = (placeholder: string, text: string) => fireEvent.changeText(screen.getByPlaceholderText(placeholder), text);

  test('invalid input is rejected on the device with the same messages the service uses', async () => {
    app = await startApp();
    await openForm();

    await type('Recipient name', '');
    await type('Country code (two letters)', 'ITA');
    await type('Weight in kg', '45');
    await press('Create shipment');

    await screen.findByText('RecipientName is required.');
    expect(screen.getByText('CountryCode must be a two-letter ISO code.')).toBeTruthy();
    expect(screen.getByText('WeightKg must be greater than 0 and at most 30.')).toBeTruthy();
    expect(screen.getByPlaceholderText('Recipient name')).toBeTruthy();
    expect(await (await http(`${app.backendUrl}/api/shipments`)).json()).toHaveLength(3);
  });

  test('a weight typed with a decimal comma is understood on a German device', async () => {
    app = await startApp({ locale: 'de-DE' });
    await openForm();

    await type('Recipient name', 'Maria Rossi');
    await type('Country code (two letters)', 'IT');
    await type('Weight in kg', '2,5');
    await press('Get quote');

    await waitFor(() => expect(screen.getByTestId('quote')).toHaveTextContent('12.30 EUR'));
    expect(screen.queryByText(/must be|is required/)).toBeNull();
  });

  test('saving creates the shipment under the device identity and opens its detail', async () => {
    app = await startApp({ clientId: 'device-42' });
    await openForm();

    await type('Recipient name', 'Maria Rossi');
    await type('City', 'Milano');
    await type('Postal code', '20121');
    await type('Country code (two letters)', 'IT');
    await type('Weight in kg', '2');
    await fireEvent(screen.getByLabelText('Express'), 'valueChange', true);
    await press('Create shipment');

    await screen.findByText('Created by device-42');
    expect(screen.getByTestId('status')).toHaveTextContent('Created');
    expect(screen.getByText('16.65 EUR')).toBeTruthy();
    await waitFor(() => expect(rows()).toEqual(['SHP-000001', 'SHP-000002', 'SHP-000003', 'SHP-000004']));
    // The form was replaced by the detail screen, not left underneath it.
    expect(screen.queryByPlaceholderText('Recipient name', hidden)).toBeNull();
  });
});
