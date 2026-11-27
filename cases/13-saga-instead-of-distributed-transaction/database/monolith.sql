-- Before: one database. Stock, orders and payments can change in one transaction.
CREATE SCHEMA monolith;
CREATE TABLE monolith.stock (sku text PRIMARY KEY, on_hand int NOT NULL CHECK (on_hand >= 0));
CREATE TABLE monolith.orders (
    id uuid PRIMARY KEY, request_id uuid NOT NULL UNIQUE, customer text NOT NULL,
    total numeric(12, 2) NOT NULL, status text NOT NULL, reason text
);
CREATE TABLE monolith.order_lines (order_id uuid NOT NULL REFERENCES monolith.orders (id), sku text NOT NULL, quantity int NOT NULL);
CREATE TABLE monolith.payments (order_id uuid PRIMARY KEY REFERENCES monolith.orders (id), amount numeric(12, 2) NOT NULL);
INSERT INTO monolith.stock VALUES ('CHAIR', 10), ('DESK', 2), ('LAMP', 1);
