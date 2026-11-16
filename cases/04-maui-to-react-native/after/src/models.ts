export type ShipmentStatus = 'Created' | 'InTransit' | 'Delivered' | 'Returned';
export type ServiceLevel = 'Standard' | 'Express';

export interface Shipment {
  trackingNumber: string;
  recipientName: string;
  city: string | null;
  postalCode: string | null;
  countryCode: string;
  weightKg: number;
  service: ServiceLevel;
  status: ShipmentStatus;
  price: number;
  createdBy: string;
  createdUtc: string;
  deliveredUtc: string | null;
}

export interface NewShipment {
  recipientName: string;
  city: string;
  postalCode: string;
  countryCode: string;
  weightKg: number;
  service: ServiceLevel;
}

export const STATUS_FILTERS = ['All', 'Created', 'InTransit', 'Delivered', 'Returned'] as const;
export type StatusFilter = (typeof STATUS_FILTERS)[number];

export const statusText = (status: string) => (status === 'InTransit' ? 'In transit' : status);

// In the .NET app these were computed properties on the Shipment class. Here they are plain functions.
export const shipmentTitle = (s: Shipment) => `${s.trackingNumber} · ${s.recipientName}`;
export const shipmentSubtitle = (s: Shipment) => `${s.city}, ${s.countryCode} · ${statusText(s.status)}`;

export const statusColor = (status: string) =>
  ({ Delivered: '#2E7D32', InTransit: '#1F6FD6', Returned: '#C62828' } as Record<string, string>)[status] ?? '#616161';

/** "Delivered 2026-02-10 12:00 UTC", always in UTC and independent of the device locale. */
export const deliveredText = (s: Shipment | null) =>
  s?.deliveredUtc ? `Delivered ${new Date(s.deliveredUtc).toISOString().slice(0, 16).replace('T', ' ')} UTC` : '';
