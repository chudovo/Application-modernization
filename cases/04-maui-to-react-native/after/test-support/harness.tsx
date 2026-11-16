import React from 'react';
import { render } from '@testing-library/react-native';
import { AppRoot } from '../src/AppRoot';
import type { Services } from '../src/services/services';
import { createShipmentApi } from '../src/services/shipmentApi';
import { http } from './http';

export { http };

export interface TestApp {
  /** Base address of this test's own copy of the shipment service, to act as "another user". */
  backendUrl: string;
  alerts: string[];
  setOnline(online: boolean): void;
  stop(): Promise<void>;
}

/**
 * Renders the real app (navigation and all three screens) against a fresh copy of the shipment service from case 02.
 * Only the device-level services are replaced: connectivity, key-value storage, dialogs and the device locale.
 */
export async function startApp(options: { online?: boolean; clientId?: string; locale?: string } = {}): Promise<TestApp> {
  const control = process.env.TEST_BACKEND_URL!;
  const { id, url } = await (await http(`${control}/backends`, { method: 'POST' })).json();

  let online = options.online ?? true;
  const storage = new Map<string, string>([['client.id', options.clientId ?? 'device-1']]);
  const alerts: string[] = [];

  const services: Services = {
    api: createShipmentApi(url, http),
    network: { isOnline: async () => online },
    settings: { get: async (key) => storage.get(key) ?? null, set: async (key, value) => void storage.set(key, value) },
    dialogs: { alert: async (message) => void alerts.push(message) },
    locale: () => options.locale ?? 'en-US',
  };

  await render(<AppRoot services={services} />);

  return {
    backendUrl: url,
    alerts,
    setOnline: (value) => void (online = value),
    stop: async () => void (await http(`${control}/backends/${id}`, { method: 'DELETE' })),
  };
}
