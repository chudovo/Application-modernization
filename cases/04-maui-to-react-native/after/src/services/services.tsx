import React, { createContext, useContext } from 'react';
import type { ShipmentApi } from './shipmentApi';

/**
 * Everything the screens need from outside. In the .NET MAUI app these were interfaces resolved by dependency
 * injection (IShipmentApi, INetworkStatus, ISettings, IDialogs). Here they are one object passed through React context:
 * the app supplies real implementations, the tests supply their own.
 */
export interface Services {
  api: ShipmentApi;
  network: { isOnline(): Promise<boolean> };
  settings: { get(key: string): Promise<string | null>; set(key: string, value: string): Promise<void> };
  dialogs: { alert(message: string): Promise<void> };
  /** BCP 47 tag of the device, used to read numbers the way the user types them. */
  locale(): string;
}

const ServicesContext = createContext<Services | null>(null);

export const ServicesProvider = ({ services, children }: { services: Services; children: React.ReactNode }) => (
  <ServicesContext.Provider value={services}>{children}</ServicesContext.Provider>
);

export function useServices(): Services {
  const services = useContext(ServicesContext);
  if (!services) throw new Error('ServicesProvider is missing');
  return services;
}
