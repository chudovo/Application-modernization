import type { NewShipment, Shipment } from '../models';

export class ApiError extends Error {
  constructor(
    readonly code: string,
    message: string,
    readonly errors: string[] = [],
  ) {
    super(message);
  }
}

export interface ShipmentApi {
  find(status?: string): Promise<Shipment[]>;
  get(trackingNumber: string): Promise<Shipment>;
  create(shipment: NewShipment, clientId?: string | null): Promise<Shipment>;
  updateStatus(trackingNumber: string, status: string): Promise<Shipment>;
  quote(weightKg: number, countryCode: string, service: string): Promise<number>;
}

/** `fetchImpl` exists for tests: the Expo test environment replaces the global fetch with a stub that makes no requests. */
export function createShipmentApi(baseUrl: string, fetchImpl: typeof fetch = fetch): ShipmentApi {
  async function send<T>(method: string, path: string, body?: unknown, clientId?: string | null): Promise<T> {
    const headers: Record<string, string> = { Accept: 'application/json' };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    if (clientId) headers['X-Client-Id'] = clientId;

    const response = await fetchImpl(baseUrl.replace(/\/$/, '') + path, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    const text = await response.text();
    const json = text ? JSON.parse(text) : {};

    if (!response.ok) {
      throw new ApiError(json.code ?? `http_${response.status}`, json.title ?? 'The request failed.', json.errors ?? []);
    }
    return json as T;
  }

  return {
    find: (status) => send('GET', '/api/shipments' + (status ? `?status=${encodeURIComponent(status)}` : '')),
    get: (trackingNumber) => send('GET', `/api/shipments/${encodeURIComponent(trackingNumber)}`),
    create: (shipment, clientId) => send('POST', '/api/shipments', shipment, clientId),
    updateStatus: (trackingNumber, status) =>
      send('PUT', `/api/shipments/${encodeURIComponent(trackingNumber)}/status`, { status }),
    quote: async (weightKg, countryCode, service) =>
      (await send<{ price: number }>('POST', '/api/rates/quote', { weightKg, countryCode, service })).price,
  };
}
