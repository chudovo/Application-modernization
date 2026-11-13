using System.Net;
using ParcelTracker.iOS.Services;
using ParcelTracker.Services;
using SystemConfiguration;
using Xamarin.Forms;

[assembly: Dependency(typeof(ConnectivityService))]

namespace ParcelTracker.iOS.Services
{
    public class ConnectivityService : IConnectivityService
    {
        public bool IsOnline
        {
            get
            {
                using (var reachability = new NetworkReachability(new IPAddress(0)))
                {
                    NetworkReachabilityFlags flags;
                    return reachability.TryGetFlags(out flags)
                        && (flags & NetworkReachabilityFlags.Reachable) != 0
                        && (flags & NetworkReachabilityFlags.ConnectionRequired) == 0;
                }
            }
        }
    }
}
