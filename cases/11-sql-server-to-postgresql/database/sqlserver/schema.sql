-- The database as it runs on SQL Server. The server collation is the default one, case-insensitive.
CREATE TABLE customers (
    id            int IDENTITY(1,1) PRIMARY KEY,
    name          nvarchar(100) NOT NULL,
    email         nvarchar(200) NOT NULL,
    region        varchar(2) NOT NULL,
    vip           bit NOT NULL DEFAULT 0,
    note          nvarchar(10) NULL,
    last_order_at datetime NULL,
    CONSTRAINT uq_customers_email UNIQUE (email)
);

CREATE TABLE orders (
    id          int IDENTITY(1,1) PRIMARY KEY,
    customer_id int NOT NULL REFERENCES customers (id),
    placed_at   datetime NOT NULL,
    items       int NOT NULL,
    total       money NOT NULL
);

CREATE TABLE audit (
    id      int IDENTITY(1,1) PRIMARY KEY,
    at      datetime NOT NULL,
    message nvarchar(200) NOT NULL
);
