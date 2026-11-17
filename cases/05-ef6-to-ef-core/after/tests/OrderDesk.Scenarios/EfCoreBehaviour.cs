using OrderDesk.Data;
using Xunit;

namespace OrderDesk.Scenarios;

/// <summary>Behaviour of the EF Core version that the Entity Framework 6 version does not share.</summary>
public class EfCoreBehaviour : IDisposable
{
    private readonly TestDatabase _database = TestDatabase.Create();
    public void Dispose() => _database.Dispose();

    [Fact]
    public void An_order_created_at_a_millisecond_is_found_by_its_creation_time()
    {
        var store = new OrderStore(_database.ConnectionString);

        // The property is mapped to datetime, so EF Core sends the value as datetime and the comparison is exact.
        // The Entity Framework 6 version finds nothing here.
        Assert.Equal([1], store.FindOrderIdsCreatedAt(new DateTime(2026, 3, 2, 9, 15, 0, 123)));
    }
}
