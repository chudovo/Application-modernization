// Replaces the typed ShipmentChangedMessage sent through IMessenger: a screen that changes a shipment announces it,
// and the list reloads. One event does not justify a state management library.
type Listener = () => void;

const listeners = new Set<Listener>();

export const shipmentChanged = {
  emit: () => listeners.forEach((listener) => listener()),
  subscribe(listener: Listener): () => void {
    listeners.add(listener);
    return () => listeners.delete(listener);
  },
};
