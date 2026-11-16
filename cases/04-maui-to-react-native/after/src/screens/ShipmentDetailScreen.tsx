import React, { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, Pressable, ScrollView, StyleSheet, Text } from 'react-native';
import { Shipment, deliveredText, statusColor, statusText } from '../models';
import type { ScreenProps } from '../navigation';
import { shipmentChanged } from '../services/events';
import { useServices } from '../services/services';
import { ApiError } from '../services/shipmentApi';

export function ShipmentDetailScreen({ route }: ScreenProps<'Detail'>) {
  const { trackingNumber } = route.params;
  const { api, dialogs } = useServices();
  const [shipment, setShipment] = useState<Shipment | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    setBusy(true);
    try {
      setShipment(await api.get(trackingNumber));
    } catch (e) {
      if (e instanceof ApiError) await dialogs.alert(e.message);
      else throw e;
    } finally {
      setBusy(false);
    }
  }, [api, dialogs, trackingNumber]);

  useEffect(() => {
    void load();
  }, [load]);

  const changeStatus = async (status: string) => {
    setBusy(true);
    try {
      setShipment(await api.updateStatus(trackingNumber, status));
      shipmentChanged.emit();
    } catch (e) {
      if (!(e instanceof ApiError)) throw e;
      // Someone else changed the shipment in the meantime: tell the user and show the current state.
      await dialogs.alert(e.message);
      await load();
    } finally {
      setBusy(false);
    }
  };

  return (
    <ScrollView contentContainerStyle={styles.content}>
      {shipment && (
        <>
          <Text style={styles.name}>{shipment.recipientName}</Text>
          <Text>{shipment.city}</Text>
          <Text>{shipment.countryCode}</Text>
          <Text testID="status" style={[styles.status, { color: statusColor(shipment.status) }]}>
            {statusText(shipment.status)}
          </Text>
          {shipment.deliveredUtc && <Text testID="delivered">{deliveredText(shipment)}</Text>}
          <Text>{shipment.price.toFixed(2)} EUR</Text>
          <Text style={styles.muted}>Created by {shipment.createdBy}</Text>
          {shipment.status === 'Created' && (
            <Pressable accessibilityRole="button" style={styles.button} onPress={() => changeStatus('InTransit')}>
              <Text style={styles.buttonText}>Mark in transit</Text>
            </Pressable>
          )}
          {shipment.status === 'InTransit' && (
            <Pressable accessibilityRole="button" style={styles.button} onPress={() => changeStatus('Delivered')}>
              <Text style={styles.buttonText}>Mark delivered</Text>
            </Pressable>
          )}
        </>
      )}
      {busy && <ActivityIndicator />}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  content: { padding: 16, gap: 8 },
  name: { fontSize: 22, fontWeight: 'bold' },
  status: { fontWeight: 'bold' },
  muted: { color: '#616161' },
  button: { backgroundColor: '#1F6FD6', borderRadius: 8, padding: 12, alignItems: 'center' },
  buttonText: { color: 'white', fontWeight: 'bold' },
});
