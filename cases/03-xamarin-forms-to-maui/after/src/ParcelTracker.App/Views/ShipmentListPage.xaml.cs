using ParcelTracker.ViewModels;

namespace ParcelTracker.App.Views;

public partial class ShipmentListPage : ContentPage
{
    private readonly ShipmentListViewModel _viewModel;

    // The view model is injected. The page no longer creates it.
    public ShipmentListPage(ShipmentListViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }
}
