using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OrderDesk.Data
{
    // Written the way Entity Framework 6 Code First models usually were: a few attributes, everything else left to
    // conventions, navigation properties virtual so that they load on first access.

    public class Customer
    {
        public int Id { get; set; }
        [Required, MaxLength(200)] public string Name { get; set; }
        [MaxLength(200)] public string Email { get; set; }
        public Address Address { get; set; } = new Address();
        public bool IsActive { get; set; }
        public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
    }

    [ComplexType]
    public class Address
    {
        [MaxLength(200)] public string Street { get; set; }
        [MaxLength(100)] public string City { get; set; }
        [MaxLength(20)] public string PostalCode { get; set; }
        [MaxLength(2)] public string CountryCode { get; set; }
    }

    public class Order
    {
        public int Id { get; set; }

        // No foreign key property: the column is named by convention, Customer_Id.
        [Required] public virtual Customer Customer { get; set; }

        public DateTime CreatedUtc { get; set; }
        public OrderStatus Status { get; set; }
        public decimal Total { get; set; }
        [MaxLength(500)] public string Note { get; set; }
        [Timestamp] public byte[] RowVersion { get; set; }

        public virtual ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();

        // Many-to-many without a join entity: the table TagOrders is created by convention.
        public virtual ICollection<Tag> Tags { get; set; } = new List<Tag>();
    }

    public enum OrderStatus { New = 0, Confirmed = 1, Shipped = 2, Cancelled = 3 }

    public class OrderLine
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public virtual Order Order { get; set; }
        [Required, MaxLength(50)] public string Sku { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    public class Tag
    {
        public int Id { get; set; }
        [Required, MaxLength(50), Index(IsUnique = true)] public string Name { get; set; }
        public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
    }
}
