using ParcelTracker.Services;
using ParcelTracker.Views;
using Xamarin.Forms;

namespace ParcelTracker
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            // The API client is registered here. Connectivity and settings are registered by each platform project
            // with [assembly: Dependency(...)], because they have a separate implementation per platform.
            DependencyService.Register<IShipmentApi, ShipmentApi>();

            MainPage = new NavigationPage(new ShipmentListPage());
        }
    }
}
