using System;
using System.Linq;

namespace OrderDesk.Domain
{
    public class PricingService
    {
        public const decimal GoldDiscountPercent = 5m;
        public const decimal VolumeDiscountPercent = 2m;
        public const decimal VolumeThreshold = 1000m;

        public void Price(Order order, Customer customer)
        {
            var subtotal = order.Lines.Sum(l => l.Quantity * l.UnitPrice);

            var discount = 0m;
            if (customer.Tier == CustomerTier.Gold) discount += GoldDiscountPercent;
            if (subtotal >= VolumeThreshold) discount += VolumeDiscountPercent;

            var net = Round(subtotal * (1 - discount / 100m));
            var vat = customer.VatExempt ? 0m : Round(net * AppSettings.VatRate);

            order.Subtotal = Round(subtotal);
            order.DiscountPercent = discount;
            order.NetTotal = net;
            order.VatTotal = vat;
            order.GrossTotal = net + vat;
        }

        private static decimal Round(decimal value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }
    }
}
