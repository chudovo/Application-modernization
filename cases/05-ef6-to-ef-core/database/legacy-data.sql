-- Rows as the Entity Framework 6 application wrote them. Both versions are tested against this data.
SET IDENTITY_INSERT dbo.Customers ON;
INSERT dbo.Customers (Id, Name, Email, Address_Street, Address_City, Address_PostalCode, Address_CountryCode, IsActive) VALUES
 (1, N'Nordwind Handel GmbH', N'einkauf@nordwind.example', N'Speicherstraße 12', N'Hamburg', N'20457', N'DE', 1),
 (2, N'Atelier Dubois',       N'contact@dubois.example',  N'8 rue Mercière',    N'Lyon',    N'69002', N'FR', 1),
 (3, N'Kowalski Logistics',   NULL,                       NULL,                 N'Gdańsk',  NULL,     N'PL', 0);
SET IDENTITY_INSERT dbo.Customers OFF;

SET IDENTITY_INSERT dbo.Tags ON;
INSERT dbo.Tags (Id, Name) VALUES (1, N'priority'), (2, N'wholesale');
SET IDENTITY_INSERT dbo.Tags OFF;

SET IDENTITY_INSERT dbo.Orders ON;
INSERT dbo.Orders (Id, Customer_Id, CreatedUtc, Status, Total, Note) VALUES
 (1, 1, '2026-03-02T09:15:00.123', 1, 707.00, N'Deliver to the back entrance'),
 (2, 1, '2026-03-02T14:40:00.000', 0,  79.98, NULL),
 (3, 2, '2026-03-02T16:05:00.000', 3, 349.00, NULL),
 (4, 2, '2026-03-03T08:00:00.000', 2, 360.00, NULL);
SET IDENTITY_INSERT dbo.Orders OFF;

INSERT dbo.OrderLines (OrderId, Sku, Quantity, UnitPrice) VALUES
 (1, N'CHAIR-01', 4,  89.50),
 (1, N'DESK-02',  1, 349.00),
 (2, N'LAMP-07',  2,  39.99),
 (3, N'DESK-02',  1, 349.00),
 (4, N'SHELF-03', 3, 120.00);

INSERT dbo.TagOrders (Tag_Id, Order_Id) VALUES (1, 1), (2, 1), (2, 2), (1, 4);
