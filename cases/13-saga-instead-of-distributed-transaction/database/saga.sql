-- What the saga adds to the tables of the three services.

-- Payments: one capture per order, however often it is asked for.
CREATE UNIQUE INDEX uq_payments_order ON payments.payments (order_id);

-- Orders: where each order is in its sequence of steps. Written after every step.
CREATE TABLE orders.sagas (
    order_id uuid PRIMARY KEY REFERENCES orders.orders (id),
    step text NOT NULL,                       -- reserve, capture, confirm, refund, release, reject, done
    attempts int NOT NULL DEFAULT 0,
    reason text
);
