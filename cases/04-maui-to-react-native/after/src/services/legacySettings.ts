import type { Shipment } from '../models';

/**
 * What the .NET MAUI app left on the device, read through the small native module in modules/legacy-settings:
 * on Android a SharedPreferences file named "parceltracker", on iOS the standard user defaults.
 */
export interface LegacyStore {
  getString(key: string): Promise<string | null>;
}

export interface KeyValueStore {
  get(key: string): Promise<string | null>;
  set(key: string, value: string): Promise<void>;
}

const MIGRATED_FLAG = 'legacy.migrated';

/**
 * Runs once, on the first start after the update. Copies the device identity and the offline cache from where the
 * old app kept them into the new app's storage. Without it every user loses both on update.
 */
export async function migrateLegacySettings(legacy: LegacyStore | null, settings: KeyValueStore): Promise<void> {
  if ((await settings.get(MIGRATED_FLAG)) === 'true') return;

  if (legacy) {
    const clientId = await legacy.getString('client.id');
    if (clientId && !(await settings.get('client.id'))) await settings.set('client.id', clientId);

    const cache = await legacy.getString('shipments.cache');
    if (cache && !(await settings.get('shipments.cache'))) {
      const converted = convertLegacyCache(cache);
      if (converted) await settings.set('shipments.cache', JSON.stringify(converted));
    }
  }

  await settings.set(MIGRATED_FLAG, 'true');
}

/**
 * The old app wrote its cache with .NET's default JSON settings: PascalCase property names, plus the computed
 * Title and Subtitle properties. The new app expects the camelCase shape the API returns. A cache that cannot be
 * read is dropped: it is only a cache, and the next online load rebuilds it.
 */
export function convertLegacyCache(json: string): Shipment[] | null {
  try {
    const parsed: unknown = JSON.parse(json);
    if (!Array.isArray(parsed)) return null;
    return parsed.map((item: Record<string, unknown>) => ({
      trackingNumber: String(item.TrackingNumber),
      recipientName: String(item.RecipientName),
      city: (item.City as string | null) ?? null,
      postalCode: (item.PostalCode as string | null) ?? null,
      countryCode: String(item.CountryCode),
      weightKg: Number(item.WeightKg),
      service: item.Service as Shipment['service'],
      status: item.Status as Shipment['status'],
      price: Number(item.Price),
      createdBy: String(item.CreatedBy),
      createdUtc: String(item.CreatedUtc),
      deliveredUtc: (item.DeliveredUtc as string | null) ?? null,
    }));
  } catch {
    return null;
  }
}
