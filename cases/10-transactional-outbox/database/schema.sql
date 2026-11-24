-- Two services, each with its own tables. In production these are two databases; here they are two schemas.

CREATE SCHEMA orders;
CREATE TABLE orders.orders (
    id         uuid PRIMARY KEY,
    customer   text NOT NULL,
    total      numeric(12, 2) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

-- Added for the outbox: events waiting to be published, written in the same transaction as the order.
CREATE TABLE orders.outbox (
    id           uuid PRIMARY KEY,
    type         text NOT NULL,
    payload      jsonb NOT NULL,
    created_at   timestamptz NOT NULL DEFAULT now(),
    published_at timestamptz
);
CREATE INDEX ix_outbox_unpublished ON orders.outbox (created_at) WHERE published_at IS NULL;

CREATE SCHEMA shipping;
CREATE TABLE shipping.shipments (
    id         bigserial PRIMARY KEY,
    order_id   uuid NOT NULL,
    customer   text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

-- Added for the idempotent consumer: the messages already handled.
CREATE TABLE shipping.processed_messages (
    message_id   uuid PRIMARY KEY,
    processed_at timestamptz NOT NULL DEFAULT now()
);
