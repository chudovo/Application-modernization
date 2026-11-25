-- Rows as years of use left them: mixed case, an umlaut, customers who never ordered.
SET IDENTITY_INSERT customers ON;
INSERT customers (id, name, email, region, vip, note, last_order_at) VALUES
 (1, N'Anna Schmidt',   N'anna@example.com',     'DE', 1, N'key acct', '2026-05-10T23:30:00'),
 (2, N'bernd Meier',    N'Bernd.Meier@Example.com', 'DE', 0, NULL,     '2026-04-02T09:00:00'),
 (3, N'Émile Dubois',   N'emile@example.fr',     'FR', 0, NULL,        NULL),
 (4, N'Zoë Ärmel',      N'zoe@example.de',       'DE', 0, N'call first', NULL),
 (5, N'carla Rossi',    N'carla@example.it',     'IT', 1, NULL,        '2026-05-09T12:00:00'),
 (6, N'Österreich GmbH', N'office@example.at',   'AT', 0, NULL,        '2026-01-15T08:00:00');
SET IDENTITY_INSERT customers OFF;

SET IDENTITY_INSERT orders ON;
INSERT orders (id, customer_id, placed_at, items, total) VALUES
 (1, 1, '2026-05-10T23:30:00', 2, 199.90),
 (2, 1, '2026-03-01T10:00:00', 3, 349.00),
 (3, 2, '2026-04-02T09:00:00', 1,  39.99),
 (4, 5, '2026-05-09T12:00:00', 4, 120.50),
 (5, 6, '2026-01-15T08:00:00', 3,  15.00);
SET IDENTITY_INSERT orders OFF;
