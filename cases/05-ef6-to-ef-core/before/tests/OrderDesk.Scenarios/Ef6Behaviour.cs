using OrderDesk.Data;
using Xunit;

namespace OrderDesk.Scenarios;

/// <summary>Behaviour of the Entity Framework 6 version that the EF Core version does not share.</summary>
public class Ef6Behaviour : IDisposable
{
    private readonly TestDatabase _database = TestDatabase.Create();
    public void Dispose() => _database.Dispose();

    [Fact]
    public void An_order_created_at_a_millisecond_is_not_found_by_its_creation_time()
    {
        var store = new OrderStore(_database.ConnectionString);

        // Order 1 was created at 09:15:00.123 and the column is datetime. Entity Framework 6 sends the value as
        // datetime2, SQL Server converts the column value to 09:15:00.1233333 for the comparison, and nothing matches.
        Assert.Empty(store.FindOrderIdsCreatedAt(new DateTime(2026, 3, 2, 9, 15, 0, 123)));
    }
}
