using ParcelTracker.ViewModels;

namespace ParcelTracker.App.Views;

// The tracking number arrives as a Shell route parameter instead of a constructor argument.
[QueryProperty(nameof(TrackingNumber), "trackingNumber")]
public partial class ShipmentDetailPage : ContentPage
{
    private readonly ShipmentDetailViewModel _viewModel;

    public ShipmentDetailPage(ShipmentDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public string TrackingNumber
    {
        set => _viewModel.Initialize(value);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }
}
