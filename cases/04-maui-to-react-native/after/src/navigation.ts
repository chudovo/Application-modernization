import type { NativeStackScreenProps } from '@react-navigation/native-stack';

export type RootStackParamList = {
  Shipments: undefined;
  Detail: { trackingNumber: string };
  NewShipment: undefined;
};

export type ScreenProps<T extends keyof RootStackParamList> = NativeStackScreenProps<RootStackParamList, T>;
