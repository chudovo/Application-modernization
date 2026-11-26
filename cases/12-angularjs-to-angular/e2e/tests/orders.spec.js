// What a user of the order desk sees and does. The same file runs against the AngularJS application and against
// the Angular one. It knows only what is on the page: texts, the data-test attributes, and what reaches the server.
const { test, expect } = require('@playwright/test');

const column = (page, name) => page.locator(`[data-test="row"] [data-test="${name}"]`);
const serverLog = async (request) => (await (await request.get('/api/_log')).json()).filter((entry) => !entry.url.startsWith('/api/_'));

test.beforeEach(async ({ request }) => {
  await request.post('/api/_reset');
});

test.describe('the list', () => {
  test('shows every order, newest first, with amounts and dates formatted as before', async ({ page }) => {
    await page.goto('/');

    await expect(column(page, 'customer')).toHaveText(['bernd Meier', 'Anna Schmidt', 'carla Rossi', 'Zoë Ärmel', 'Émile Dubois']);
    await expect(column(page, 'total')).toHaveText(['$89.00', '$1,249.50', '', '$349.00', '$15.25']);
    await expect(column(page, 'placed')).toHaveText(['Mar 4, 2026', 'Mar 2, 2026', 'Mar 1, 2026', 'Feb 11, 2026', 'Jan 20, 2026']);
    await expect(page.locator('[data-test="count"]')).toHaveText('5 orders');
  });

  test('search looks at every field and ignores letter case', async ({ page }) => {
    await page.goto('/');
    await expect(column(page, 'customer')).toHaveCount(5);

    await page.locator('[data-test="search"]').fill('BERLIN');   // a city, typed in capitals
    await expect(column(page, 'customer')).toHaveText(['Anna Schmidt', 'Zoë Ärmel']);
    await expect(page.locator('[data-test="count"]')).toHaveText('2 orders');

    await page.locator('[data-test="search"]').fill('shipped');   // a status
    await expect(column(page, 'customer')).toHaveText(['bernd Meier', 'Émile Dubois']);
  });

  test('the count is worded for none, one and many', async ({ page }) => {
    await page.goto('/');
    await page.locator('[data-test="search"]').fill('lyon');
    await expect(page.locator('[data-test="count"]')).toHaveText('1 order');

    await page.locator('[data-test="search"]').fill('no such thing');
    await expect(page.locator('[data-test="count"]')).toHaveText('No orders');
  });

  test('a click on Customer sorts by name without regard to letter case, a second click reverses', async ({ page }) => {
    await page.goto('/');
    await expect(column(page, 'customer')).toHaveCount(5);

    await page.locator('[data-test="sort-customer"]').click();
    await expect(column(page, 'customer')).toHaveText(SORTED_BY_CUSTOMER);

    await page.locator('[data-test="sort-customer"]').click();
    await expect(column(page, 'customer')).toHaveText([...SORTED_BY_CUSTOMER].reverse());
  });

  test('a click on Total sorts by amount, the order without an amount last', async ({ page }) => {
    await page.goto('/');
    await expect(column(page, 'customer')).toHaveCount(5);

    await page.locator('[data-test="sort-total"]').click();

    await expect(column(page, 'total')).toHaveText(['$15.25', '$89.00', '$349.00', '$1,249.50', '']);
  });

  test('the status filter asks the server, and "All" sends no status at all', async ({ page, request }) => {
    await page.goto('/');
    await expect(column(page, 'customer')).toHaveCount(5);

    await page.locator('[data-test="status"]').selectOption('shipped');
    await expect(column(page, 'customer')).toHaveText(['bernd Meier', 'Émile Dubois']);

    await page.locator('[data-test="status"]').selectOption('');
    await expect(column(page, 'customer')).toHaveCount(5);

    const requests = (await serverLog(request)).map((entry) => `${entry.method} ${entry.url}`);
    expect(requests).toEqual(['GET /api/orders', 'GET /api/orders?status=shipped', 'GET /api/orders']);
  });
});

// Filled in from what the AngularJS application does.
// Letter case is ignored, but É comes after Z: AngularJS compares lower-cased strings character by character.
const SORTED_BY_CUSTOMER = ['Anna Schmidt', 'bernd Meier', 'carla Rossi', 'Zoë Ärmel', 'Émile Dubois'];

test.describe('one order', () => {
  test('a bookmark from the old application still opens the order', async ({ page }) => {
    await page.goto('/#!/orders/3');

    await expect(page.locator('[data-test="title"]')).toHaveText('Order 3: Zoë Ärmel');
    await expect(page.locator('[data-test="summary"]')).toHaveText('$349.00, placed Feb 11, 2026 8:00:00 AM');
  });

  test('a click in the list opens the order and Back returns to the list', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('link', { name: 'Anna Schmidt' }).click();
    await expect(page.locator('[data-test="title"]')).toHaveText('Order 1: Anna Schmidt');

    await page.locator('[data-test="back"]').click();
    await expect(column(page, 'customer')).toHaveCount(5);
  });

  test('the note keeps its formatting and loses anything that could run', async ({ page }) => {
    await page.goto('/#!/orders/2');
    const note = page.locator('[data-test="note"]');

    await expect(note).toHaveText('Call the office first.');
    await expect(note.locator('a')).toHaveAttribute('href', 'tel:+4940123456');
    expect(await note.locator('script, img[onerror]').count()).toBe(0);
    expect(await page.evaluate(() => window.hacked)).toBeUndefined();

    await page.goto('/#!/orders/1');
    await expect(page.locator('[data-test="note"] b')).toHaveText('back entrance');
  });

  test('an unknown order says so', async ({ page }) => {
    await page.goto('/#!/orders/999');

    await expect(page.locator('[data-test="not-found"]')).toHaveText('Order not found');
    await expect(page.locator('[data-test="title"]')).toHaveCount(0);
  });

  test('the discount is validated as it is typed and blocks saving', async ({ page }) => {
    await page.goto('/#!/orders/1');
    const discount = page.locator('[data-test="discount"]');
    await expect(discount).toHaveValue('0');
    await expect(page.locator('[data-test="discount-error"]')).toHaveCount(0);

    await discount.fill('');
    await expect(page.locator('[data-test="discount-error"]')).toHaveText('Discount is required');
    await expect(page.locator('[data-test="save"]')).toBeDisabled();

    await discount.fill('60');
    await expect(page.locator('[data-test="discount-error"]')).toHaveText('Discount must be between 0 and 50');
    await expect(page.locator('[data-test="save"]')).toBeDisabled();

    await discount.fill('15');
    await expect(page.locator('[data-test="discount-error"]')).toHaveCount(0);
    await expect(page.locator('[data-test="save"]')).toBeEnabled();
  });

  test('saving sends numbers as numbers, with the XSRF token, and the list shows the change', async ({ page, request }) => {
    await page.goto('/#!/orders/1');
    await page.locator('[data-test="status"]').selectOption('shipped');
    await page.locator('[data-test="discount"]').fill('12.5');
    await page.locator('[data-test="save"]').click();
    await expect(page.locator('[data-test="saved"]')).toHaveText('Saved');

    const put = (await serverLog(request)).find((entry) => entry.method === 'PUT');
    expect(put.url).toBe('/api/orders/1');
    expect(put.xsrf).toBe('token-123');
    expect(JSON.parse(put.body)).toEqual({ status: 'shipped', discount: 12.5 });

    await page.locator('[data-test="back"]').click();
    await expect(page.locator('[data-test="row"]').filter({ hasText: 'Anna Schmidt' }).locator('[data-test="status-cell"]')).toHaveText('shipped');
  });
});
