using ParcelTracker.App.Views;

namespace ParcelTracker.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("detail", typeof(ShipmentDetailPage));
        Routing.RegisterRoute("new", typeof(NewShipmentPage));
    }
}
