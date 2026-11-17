using Microsoft.EntityFrameworkCore;

namespace OrderDesk.Data;

/// <summary>All data access of the order desk, on EF Core. Same public surface as the Entity Framework 6 version.</summary>
public class OrderStore(string connectionString)
{
    private OrderDeskContext Open() => OrderDeskContext.Open(connectionString);

    public int AddCustomer(string name, string email, string street, string city, string postalCode, string countryCode)
    {
        using var db = Open();
        var customer = new Customer
        {
            Name = name, Email = email, IsActive = true,
            Address = new Address { Street = street, City = city, PostalCode = postalCode, CountryCode = countryCode },
        };
        db.Customers.Add(customer);
        db.SaveChanges();
        return customer.Id;
    }

    public CustomerDetails GetCustomer(int id)
    {
        using var db = Open();
        // One query. The count is computed by the database instead of loading every order.
        return db.Customers.Where(c => c.Id == id).Select(c => new CustomerDetails
        {
            Id = c.Id, Name = c.Name, Email = c.Email,
            Street = c.Address.Street, City = c.Address.City, PostalCode = c.Address.PostalCode, CountryCode = c.Address.CountryCode,
            OrderCount = c.Orders.Count,
        }).SingleOrDefault() ?? throw new NotFoundException("Customer " + id);
    }

    public IReadOnlyList<string> SearchCustomers(string term, int page, int pageSize)
    {
        using var db = Open();
        return db.Customers
            .Where(c => c.IsActive && (c.Name.Contains(term) || c.Address.City!.Contains(term)))
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => c.Name)
            .ToList();
    }

    public int PlaceOrder(NewOrder request)
    {
        using var db = Open();
        var order = Build(db, request);
        db.Orders.Add(order);
        db.SaveChanges();
        return order.Id;
    }

    /// <summary>All orders of the batch are stored, or none.</summary>
    public IReadOnlyList<int> ImportOrders(IEnumerable<NewOrder> batch)
    {
        using var db = Open();
        using var transaction = db.Database.BeginTransaction();
        var ids = new List<int>();
        foreach (var request in batch)
        {
            var order = Build(db, request);
            db.Orders.Add(order);
            db.SaveChanges();
            ids.Add(order.Id);
        }
        transaction.Commit();
        return ids;
    }

    private static Order Build(OrderDeskContext db, NewOrder request)
    {
        var customer = db.Customers.Find(request.CustomerId) ?? throw new NotFoundException("Customer " + request.CustomerId);

        var order = new Order { Customer = customer, CreatedUtc = request.CreatedUtc, Status = OrderStatus.New };
        foreach (var line in request.Lines)
            order.Lines.Add(new OrderLine { Sku = line.Sku, Quantity = line.Quantity, UnitPrice = line.UnitPrice });
        order.Total = order.Lines.Sum(l => l.Quantity * l.UnitPrice);
        foreach (var name in request.Tags) order.Tags.Add(FindOrCreateTag(db, name));
        return order;
    }

    private static Tag FindOrCreateTag(OrderDeskContext db, string name) =>
        db.Tags.Local.FirstOrDefault(t => t.Name == name)
        ?? db.Tags.FirstOrDefault(t => t.Name == name)
        ?? db.Tags.Add(new Tag { Name = name }).Entity;

    public OrderDetails GetOrder(int id)
    {
        using var db = Open();
        // The EF6 version read the order and let Customer, Lines and Tags load on first access.
        // Here the query names them, and the split keeps it from multiplying lines by tags.
        var order = db.Orders
            .Include(o => o.Customer).Include(o => o.Lines).Include(o => o.Tags)
            .AsSplitQuery().AsNoTracking()
            .SingleOrDefault(o => o.Id == id) ?? throw new NotFoundException("Order " + id);

        return new OrderDetails
        {
            Id = order.Id,
            CustomerName = order.Customer.Name,
            City = order.Customer.Address.City,
            CreatedUtc = order.CreatedUtc,
            Status = order.Status,
            Total = order.Total,
            RowVersion = order.RowVersion,
            Lines = order.Lines.OrderBy(l => l.Id)
                .Select(l => new LineDetails { Sku = l.Sku, Quantity = l.Quantity, UnitPrice = l.UnitPrice }).ToList(),
            Tags = order.Tags.Select(t => t.Name).OrderBy(n => n).ToList(),
        };
    }

    public void TagOrder(int orderId, string tag)
    {
        using var db = Open();
        var order = db.Orders.Include(o => o.Tags).SingleOrDefault(o => o.Id == orderId) ?? throw new NotFoundException("Order " + orderId);
        if (order.Tags.All(t => t.Name != tag)) order.Tags.Add(FindOrCreateTag(db, tag));
        db.SaveChanges();
    }

    public IReadOnlyList<int> FindOrderIdsByTag(string tag)
    {
        using var db = Open();
        return db.Orders.Where(o => o.Tags.Any(t => t.Name == tag)).OrderBy(o => o.Id).Select(o => o.Id).ToList();
    }

    /// <summary>Changes the status if nobody has changed the order since <paramref name="rowVersion"/> was read.</summary>
    public void ChangeStatus(int orderId, OrderStatus status, byte[] rowVersion)
    {
        using var db = Open();
        var order = db.Orders.Find(orderId) ?? throw new NotFoundException("Order " + orderId);
        db.Entry(order).Property(o => o.RowVersion).OriginalValue = rowVersion;
        order.Status = status;
        try { db.SaveChanges(); }
        catch (DbUpdateConcurrencyException e) { throw new ConcurrencyConflictException(orderId, e); }
    }

    public void DeleteOrder(int id)
    {
        using var db = Open();
        var order = db.Orders.Find(id) ?? throw new NotFoundException("Order " + id);
        db.Orders.Remove(order);
        db.SaveChanges();
    }

    public int CountLines(int orderId)
    {
        using var db = Open();
        return db.OrderLines.Count(l => l.OrderId == orderId);
    }

    /// <summary>Revenue per customer for orders created on the given day, cancelled orders excluded.</summary>
    public IReadOnlyList<CustomerRevenue> RevenueByCustomer(DateTime day)
    {
        using var db = Open();
        var date = day.Date;
        return db.Orders
            .Where(o => o.CreatedUtc.Date == date && o.Status != OrderStatus.Cancelled)   // was DbFunctions.TruncateTime
            .GroupBy(o => o.Customer.Name)
            .Select(g => new CustomerRevenue { CustomerName = g.Key, Orders = g.Count(), Revenue = g.Sum(o => o.Total) })
            .OrderByDescending(r => r.Revenue).ThenBy(r => r.CustomerName)
            .ToList();
    }

    /// <summary>The newest orders, read with hand-written SQL into a type that is not part of the model.</summary>
    public IReadOnlyList<OrderSummary> RecentOrders(int take)
    {
        using var db = Open();
        // The interpolated value becomes a parameter, not text in the SQL.
        return db.Database.SqlQuery<OrderSummary>(
            $"""
             SELECT TOP ({take}) o.Id, c.Name AS CustomerName, o.CreatedUtc, o.Total,
                    (SELECT COUNT(*) FROM dbo.OrderLines l WHERE l.OrderId = o.Id) AS LineCount
             FROM dbo.Orders o JOIN dbo.Customers c ON c.Id = o.Customer_Id
             ORDER BY o.CreatedUtc DESC, o.Id DESC
             """).ToList();
    }

    /// <summary>Orders created at exactly this moment; used to recognise a repeated import.</summary>
    public IReadOnlyList<int> FindOrderIdsCreatedAt(DateTime createdUtc)
    {
        using var db = Open();
        return db.Orders.Where(o => o.CreatedUtc == createdUtc).OrderBy(o => o.Id).Select(o => o.Id).ToList();
    }
}
