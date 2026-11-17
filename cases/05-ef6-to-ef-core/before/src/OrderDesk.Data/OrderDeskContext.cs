using System.Data.Common;
using System.Data.Entity;
using System.Data.Entity.Migrations;

namespace OrderDesk.Data
{
    public class OrderDeskContext : DbContext
    {
        // Used by the migrations tooling, which sets the target database itself.
        public OrderDeskContext() : base("OrderDesk") { }
        public OrderDeskContext(string connectionString) : base(connectionString) { }
        public OrderDeskContext(DbConnection connection) : base(connection, contextOwnsConnection: false) { }

        public DbSet<Customer> Customers { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderLine> OrderLines { get; set; }
        public DbSet<Tag> Tags { get; set; }
    }

    /// <summary>The database was created and kept up to date by Code First Migrations.</summary>
    public sealed class Configuration : DbMigrationsConfiguration<OrderDeskContext>
    {
        public Configuration()
        {
            AutomaticMigrationsEnabled = true;
            ContextKey = "OrderDesk.Data.OrderDeskContext";
        }
    }
}
