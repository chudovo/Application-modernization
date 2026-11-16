import { convertLegacyCache, migrateLegacySettings } from '../src/services/legacySettings';

function memoryStore(initial: Record<string, string> = {}) {
  const data = new Map(Object.entries(initial));
  return {
    data,
    get: async (key: string) => data.get(key) ?? null,
    set: async (key: string, value: string) => void data.set(key, value),
  };
}

// Exactly what the .NET MAUI app wrote: System.Text.Json defaults, enum values as names, computed properties included.
const LEGACY_CACHE =
  '[{"TrackingNumber":"PT-000001","RecipientName":"Anna Schmidt","City":"Berlin","PostalCode":"10115",' +
  '"CountryCode":"DE","WeightKg":2.5,"Service":"Standard","Status":"Created","Price":7.25,"CreatedBy":"device-1",' +
  '"CreatedUtc":"2026-02-10T12:00:00Z","DeliveredUtc":null,"Title":"PT-000001 \\u00B7 Anna Schmidt","Subtitle":"Berlin, DE \\u00B7 Created"}]';

describe('settings left by the .NET MAUI version', () => {
  test('device identity and offline cache are carried over', async () => {
    const legacy = { getString: async (key: string) => ({ 'client.id': 'device-1', 'shipments.cache': LEGACY_CACHE })[key] ?? null };
    const settings = memoryStore();

    await migrateLegacySettings(legacy, settings);

    expect(settings.data.get('client.id')).toBe('device-1');
    expect(JSON.parse(settings.data.get('shipments.cache')!)).toEqual([
      {
        trackingNumber: 'PT-000001', recipientName: 'Anna Schmidt', city: 'Berlin', postalCode: '10115',
        countryCode: 'DE', weightKg: 2.5, service: 'Standard', status: 'Created', price: 7.25,
        createdBy: 'device-1', createdUtc: '2026-02-10T12:00:00Z', deliveredUtc: null,
      },
    ]);
  });

  test('runs once and never overwrites newer values', async () => {
    let reads = 0;
    const legacy = { getString: async () => (reads++, 'old-device') };
    const settings = memoryStore({ 'client.id': 'new-device' });

    await migrateLegacySettings(legacy, settings);
    const readsAfterFirstRun = reads;
    await migrateLegacySettings(legacy, settings);

    expect(settings.data.get('client.id')).toBe('new-device');
    expect(reads).toBe(readsAfterFirstRun);
  });

  test('a fresh install without the native module starts clean', async () => {
    const settings = memoryStore();
    await migrateLegacySettings(null, settings);
    expect(settings.data.has('client.id')).toBe(false);
    expect(settings.data.get('legacy.migrated')).toBe('true');
  });

  test('an unreadable cache is dropped', () => {
    expect(convertLegacyCache('not json')).toBeNull();
    expect(convertLegacyCache('{"a":1}')).toBeNull();
  });
});
