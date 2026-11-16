import { Alert } from 'react-native';
import AsyncStorage from '@react-native-async-storage/async-storage';
import NetInfo from '@react-native-community/netinfo';
import { getLocales } from 'expo-localization';
import type { Services } from './services';
import { createShipmentApi } from './shipmentApi';

export const API_BASE_URL = 'https://shipping.example.com';

export const deviceSettings = {
  get: (key: string) => AsyncStorage.getItem(key),
  set: (key: string, value: string) => AsyncStorage.setItem(key, value),
};

/** The implementations used on a real device. The tests replace all of them except the API client. */
export const deviceServices: Services = {
  api: createShipmentApi(API_BASE_URL),
  network: { isOnline: async () => (await NetInfo.fetch()).isConnected === true },
  settings: deviceSettings,
  dialogs: {
    alert: (message) =>
      new Promise<void>((resolve) => Alert.alert('Parcel Tracker', message, [{ text: 'OK', onPress: () => resolve() }])),
  },
  locale: () => getLocales()[0]?.languageTag ?? 'en-US',
};
