using System;
using System.Configuration;
using System.Data.Entity;
using Microsoft.Owin.Hosting;
using OrderDesk.Api.Infrastructure;
using OrderDesk.Data;

namespace OrderDesk.Api
{
    public static class Program
    {
        public static void Main()
        {
            Database.SetInitializer(new SeedingInitializer());
            ServiceLocator.Register(() => new OrderService(() => new OrderDeskContext()));

            var baseAddress = ConfigurationManager.AppSettings["BaseAddress"];
            using (WebApp.Start<Startup>(baseAddress))
            {
                Console.WriteLine("OrderDesk (legacy) listening on " + baseAddress);
                Console.ReadLine();
            }
        }

        private class SeedingInitializer : CreateDatabaseIfNotExists<OrderDeskContext>
        {
            protected override void Seed(OrderDeskContext context)
            {
                SeedData.Apply(context);
            }
        }
    }
}
