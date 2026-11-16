import React from 'react';
import { NavigationContainer } from '@react-navigation/native';
import { createNativeStackNavigator } from '@react-navigation/native-stack';
import type { RootStackParamList } from './navigation';
import { NewShipmentScreen } from './screens/NewShipmentScreen';
import { ShipmentDetailScreen } from './screens/ShipmentDetailScreen';
import { ShipmentListScreen } from './screens/ShipmentListScreen';
import { Services, ServicesProvider } from './services/services';

const Stack = createNativeStackNavigator<RootStackParamList>();

/** The whole app below the platform services. Tests render exactly this component. */
export function AppRoot({ services }: { services: Services }) {
  return (
    <ServicesProvider services={services}>
      <NavigationContainer>
        <Stack.Navigator screenOptions={{ headerStyle: { backgroundColor: '#1F6FD6' }, headerTintColor: 'white' }}>
          <Stack.Screen name="Shipments" component={ShipmentListScreen} />
          <Stack.Screen name="Detail" component={ShipmentDetailScreen} options={({ route }) => ({ title: route.params.trackingNumber })} />
          <Stack.Screen name="NewShipment" component={NewShipmentScreen} options={{ title: 'New shipment' }} />
        </Stack.Navigator>
      </NavigationContainer>
    </ServicesProvider>
  );
}
