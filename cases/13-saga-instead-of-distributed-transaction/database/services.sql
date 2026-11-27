-- After the split: three services, three sets of tables. No transaction spans two of them.
-- (In production these are three databases. Here they are three schemas, each used through its own connection pool.)

CREATE SCHEMA inventory;
CREATE TABLE inventory.stock (sku text PRIMARY KEY, on_hand int NOT NULL CHECK (on_hand >= 0));
CREATE TABLE inventory.reservations (
    order_id uuid NOT NULL, sku text NOT NULL, quantity int NOT NULL,
    state text NOT NULL,                      -- held, released
    PRIMARY KEY (order_id, sku)
);
INSERT INTO inventory.stock VALUES ('CHAIR', 10), ('DESK', 2), ('LAMP', 1);

CREATE SCHEMA payments;
CREATE TABLE payments.payments (
    id bigserial PRIMARY KEY, order_id uuid NOT NULL, amount numeric(12, 2) NOT NULL,
    state text NOT NULL                       -- captured, refunded
);
CREATE SCHEMA orders;
CREATE TABLE orders.orders (
    id uuid PRIMARY KEY, request_id uuid NOT NULL UNIQUE, customer text NOT NULL, card text NOT NULL,
    total numeric(12, 2) NOT NULL, status text NOT NULL, reason text      -- pending, confirmed, rejected
);
CREATE TABLE orders.order_lines (order_id uuid NOT NULL REFERENCES orders.orders (id), sku text NOT NULL, quantity int NOT NULL);
