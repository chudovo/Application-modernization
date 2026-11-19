-- The table as the first release of the application uses it. Money is a floating point number.
CREATE TABLE payments (
    id         bigserial PRIMARY KEY,
    customer   text NOT NULL,
    amount     double precision NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_payments_customer ON payments (customer);
