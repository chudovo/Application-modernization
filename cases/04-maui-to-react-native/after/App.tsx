import React, { useEffect, useState } from 'react';
import { StatusBar } from 'expo-status-bar';
import { requireOptionalNativeModule } from 'expo';
import { AppRoot } from './src/AppRoot';
import { deviceServices, deviceSettings } from './src/services/deviceServices';
import { LegacyStore, migrateLegacySettings } from './src/services/legacySettings';

export default function App() {
  const [ready, setReady] = useState(false);

  // Before the first screen: bring over what the .NET MAUI version stored on this device.
  useEffect(() => {
    const legacy = requireOptionalNativeModule<LegacyStore>('LegacySettings');
    migrateLegacySettings(legacy, deviceSettings)
      .catch(() => undefined)
      .finally(() => setReady(true));
  }, []);

  if (!ready) return null;
  return (
    <>
      <StatusBar style="light" />
      <AppRoot services={deviceServices} />
    </>
  );
}
