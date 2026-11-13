using ParcelTracker.ViewModels;

namespace ParcelTracker.App.Views;

public partial class NewShipmentPage : ContentPage
{
    public NewShipmentPage(NewShipmentViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
