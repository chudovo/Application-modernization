using Android.Content;
using Android.Net;
using ParcelTracker.Droid.Services;
using ParcelTracker.Services;
using Xamarin.Forms;

[assembly: Dependency(typeof(ConnectivityService))]

namespace ParcelTracker.Droid.Services
{
    public class ConnectivityService : IConnectivityService
    {
        public bool IsOnline
        {
            get
            {
                var manager = (ConnectivityManager)Android.App.Application.Context.GetSystemService(Context.ConnectivityService);
                var network = manager.ActiveNetworkInfo;
                return network != null && network.IsConnected;
            }
        }
    }
}
