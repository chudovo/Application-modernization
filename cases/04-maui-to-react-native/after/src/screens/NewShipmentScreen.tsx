import React, { useState } from 'react';
import { ActivityIndicator, Pressable, ScrollView, StyleSheet, Switch, Text, TextInput, View } from 'react-native';
import type { ScreenProps } from '../navigation';
import { parseLocalDecimal } from '../numbers';
import { shipmentChanged } from '../services/events';
import { useServices } from '../services/services';
import { ApiError } from '../services/shipmentApi';

export function NewShipmentScreen({ navigation }: ScreenProps<'NewShipment'>) {
  const { api, settings, locale } = useServices();
  const [recipientName, setRecipientName] = useState('');
  const [city, setCity] = useState('');
  const [postalCode, setPostalCode] = useState('');
  const [countryCode, setCountryCode] = useState('');
  const [weightText, setWeightText] = useState('');
  const [express, setExpress] = useState(false);
  const [errors, setErrors] = useState<string[]>([]);
  const [quote, setQuote] = useState('');
  const [busy, setBusy] = useState(false);

  const service = express ? 'Express' : 'Standard';
  const weight = parseLocalDecimal(weightText, locale());

  // The same three rules and the same messages as the service, so most mistakes never leave the device.
  const validate = () => {
    const found: string[] = [];
    if (!recipientName.trim()) found.push('RecipientName is required.');
    if (!/^[A-Za-z]{2}$/.test(countryCode)) found.push('CountryCode must be a two-letter ISO code.');
    if (weight === null || weight <= 0 || weight > 30) found.push('WeightKg must be greater than 0 and at most 30.');
    setErrors(found);
    return found.length === 0;
  };

  const run = async (action: () => Promise<void>) => {
    if (!validate()) return;
    setBusy(true);
    try {
      await action();
    } catch (e) {
      if (!(e instanceof ApiError)) throw e;
      setErrors(e.errors.length ? e.errors : [e.message]);
    } finally {
      setBusy(false);
    }
  };

  const getQuote = () => {
    setQuote('');
    return run(async () => setQuote(`${(await api.quote(weight!, countryCode, service)).toFixed(2)} EUR`));
  };

  const save = () =>
    run(async () => {
      const created = await api.create(
        { recipientName, city, postalCode, countryCode, weightKg: weight!, service },
        await settings.get('client.id'),
      );
      shipmentChanged.emit();
      // Replace the form with the detail screen of the new shipment.
      navigation.replace('Detail', { trackingNumber: created.trackingNumber });
    });

  return (
    <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
      <TextInput style={styles.input} placeholder="Recipient name" value={recipientName} onChangeText={setRecipientName} />
      <TextInput style={styles.input} placeholder="City" value={city} onChangeText={setCity} />
      <TextInput style={styles.input} placeholder="Postal code" value={postalCode} onChangeText={setPostalCode} />
      <TextInput style={styles.input} placeholder="Country code (two letters)" value={countryCode} onChangeText={setCountryCode} maxLength={2} autoCapitalize="characters" />
      <TextInput style={styles.input} placeholder="Weight in kg" value={weightText} onChangeText={setWeightText} keyboardType="decimal-pad" />
      <View style={styles.row}>
        <Text>Express</Text>
        <Switch accessibilityLabel="Express" value={express} onValueChange={setExpress} />
      </View>
      {errors.map((error) => (
        <Text key={error} style={styles.error}>{error}</Text>
      ))}
      {quote !== '' && <Text testID="quote" style={styles.quote}>{quote}</Text>}
      <Pressable accessibilityRole="button" style={styles.secondary} onPress={getQuote}>
        <Text>Get quote</Text>
      </Pressable>
      <Pressable accessibilityRole="button" style={styles.button} onPress={save}>
        <Text style={styles.buttonText}>Create shipment</Text>
      </Pressable>
      {busy && <ActivityIndicator />}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  content: { padding: 16, gap: 8 },
  // A plain TextInput has no underline or border on either platform, so the BorderlessEntry control and its
  // handler mapping from the .NET MAUI app have no counterpart here.
  input: { paddingVertical: 10, fontSize: 16 },
  row: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  error: { color: '#C62828' },
  quote: { fontWeight: 'bold' },
  secondary: { borderWidth: 1, borderColor: '#C9D6E8', borderRadius: 8, padding: 12, alignItems: 'center' },
  button: { backgroundColor: '#1F6FD6', borderRadius: 8, padding: 12, alignItems: 'center' },
  buttonText: { color: 'white', fontWeight: 'bold' },
});
