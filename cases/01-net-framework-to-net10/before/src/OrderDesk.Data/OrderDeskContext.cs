using System.Data.Entity;
using OrderDesk.Domain;

namespace OrderDesk.Data
{
    public class OrderDeskContext : DbContext
    {
        // "name=OrderDesk" is resolved from the <connectionStrings> section of the config file.
        public OrderDeskContext() : base("name=OrderDesk") { }

        public OrderDeskContext(string nameOrConnectionString) : base(nameOrConnectionString) { }

        public DbSet<Customer> Customers { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderLine> OrderLines { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>().Property(c => c.Name).IsRequired().HasMaxLength(200);
            modelBuilder.Entity<OrderLine>().Property(l => l.Sku).IsRequired().HasMaxLength(50);
            modelBuilder.Entity<Order>().HasRequired(o => o.Customer).WithMany().HasForeignKey(o => o.CustomerId);
            modelBuilder.Entity<Order>().HasMany(o => o.Lines).WithRequired().HasForeignKey(l => l.OrderId);
        }
    }
}
