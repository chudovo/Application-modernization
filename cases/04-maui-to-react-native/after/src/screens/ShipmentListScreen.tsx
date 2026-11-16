import React, { useCallback, useEffect, useState } from 'react';
import { FlatList, Pressable, RefreshControl, StyleSheet, Text, View } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { STATUS_FILTERS, Shipment, StatusFilter, shipmentSubtitle, shipmentTitle, statusColor, statusText } from '../models';
import type { ScreenProps } from '../navigation';
import { shipmentChanged } from '../services/events';
import { useServices } from '../services/services';
import { ApiError } from '../services/shipmentApi';

const CACHE_KEY = 'shipments.cache';

export function ShipmentListScreen({ navigation }: ScreenProps<'Shipments'>) {
  const { api, network, settings } = useServices();
  const [items, setItems] = useState<Shipment[]>([]);
  const [filter, setFilter] = useState<StatusFilter>('All');
  const [offline, setOffline] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);

  const load = useCallback(async () => {
    try {
      setError(null);
      const online = await network.isOnline();
      setOffline(!online);

      let shipments: Shipment[];
      if (!online) {
        const cached = await settings.get(CACHE_KEY);
        shipments = cached ? (JSON.parse(cached) as Shipment[]) : [];
        if (!cached) setError('You are offline. No saved shipments yet.');
        if (filter !== 'All') shipments = shipments.filter((s) => s.status === filter);
      } else {
        shipments = await api.find(filter === 'All' ? undefined : filter);
        if (filter === 'All') await settings.set(CACHE_KEY, JSON.stringify(shipments));
      }
      setItems(shipments);
    } catch (e) {
      if (e instanceof ApiError) setError(e.message);
      else throw e;
    } finally {
      setRefreshing(false);
    }
  }, [api, network, settings, filter]);

  // Loads when the screen comes into view and again when the filter changes (load depends on it).
  useFocusEffect(
    useCallback(() => {
      void load();
    }, [load]),
  );

  // Another screen changed a shipment: reload.
  useEffect(() => shipmentChanged.subscribe(() => void load()), [load]);

  useEffect(() => {
    navigation.setOptions({
      headerRight: () => (
        <Pressable accessibilityRole="button" onPress={() => navigation.navigate('NewShipment')}>
          <Text style={styles.headerAction}>New</Text>
        </Pressable>
      ),
    });
  }, [navigation]);

  return (
    <View style={styles.screen}>
      <View style={styles.filters}>
        {STATUS_FILTERS.map((status) => (
          <Pressable
            key={status}
            accessibilityRole="button"
            accessibilityLabel={`Filter ${statusText(status)}`}
            accessibilityState={{ selected: filter === status }}
            onPress={() => setFilter(status)}
            style={[styles.chip, filter === status && styles.chipSelected]}>
            <Text style={filter === status ? styles.chipTextSelected : styles.chipText}>{statusText(status)}</Text>
          </Pressable>
        ))}
      </View>
      {offline && <Text style={styles.offline}>Offline: showing saved shipments</Text>}
      {error && <Text style={styles.error}>{error}</Text>}
      <FlatList
        testID="list"
        data={items}
        keyExtractor={(s) => s.trackingNumber}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => { setRefreshing(true); void load(); }} />}
        renderItem={({ item }) => (
          <Pressable
            testID={`shipment-${item.trackingNumber}`}
            accessibilityRole="button"
            onPress={() => navigation.navigate('Detail', { trackingNumber: item.trackingNumber })}
            style={styles.row}>
            <Text style={styles.title}>{shipmentTitle(item)}</Text>
            <Text style={{ color: statusColor(item.status) }}>{shipmentSubtitle(item)}</Text>
          </Pressable>
        )}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: 'white' },
  filters: { flexDirection: 'row', flexWrap: 'wrap', gap: 8, padding: 16 },
  chip: { borderWidth: 1, borderColor: '#C9D6E8', borderRadius: 16, paddingHorizontal: 12, paddingVertical: 6 },
  chipSelected: { backgroundColor: '#1F6FD6', borderColor: '#1F6FD6' },
  chipText: { color: '#22395B' },
  chipTextSelected: { color: 'white' },
  offline: { backgroundColor: '#FFF3CD', paddingHorizontal: 16, paddingVertical: 8 },
  error: { color: '#C62828', marginHorizontal: 16, marginVertical: 8 },
  row: { paddingHorizontal: 16, paddingVertical: 12 },
  title: { fontWeight: 'bold' },
  headerAction: { color: 'white', fontSize: 16 },
});
