using System;
using System.ServiceModel;
using Shipping.Service;

namespace Shipping.Host
{
    public static class Program
    {
        public static void Main()
        {
            // Endpoints, bindings and behaviours come from the <system.serviceModel> section of App.config.
            using (var host = new ServiceHost(typeof(ShipmentService)))
            {
                host.Open();
                Console.WriteLine("ShipmentService (WCF) listening on " + host.BaseAddresses[0]);
                Console.ReadLine();
            }
        }
    }
}
