-- Rows as years of the old application left them: clean amounts, computed amounts with more than two decimals,
-- and a refund.
INSERT INTO payments (customer, amount, created_at) VALUES
 ('acme',   19.99,              '2026-01-10T09:00:00Z'),
 ('acme',   0.1,                '2026-01-11T09:00:00Z'),
 ('acme',   0.2,                '2026-01-12T09:00:00Z'),
 ('thirds', 100.0 / 3,          '2026-02-01T09:00:00Z'),
 ('thirds', 100.0 / 3,          '2026-02-02T09:00:00Z'),
 ('thirds', 100.0 / 3,          '2026-02-03T09:00:00Z'),
 ('globex', 12.345,             '2026-03-01T09:00:00Z'),
 ('globex', -5.5,               '2026-03-02T09:00:00Z');
