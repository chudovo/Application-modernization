using ParcelTracker.ViewModels;
using Xamarin.Forms;

namespace ParcelTracker.Views
{
    public partial class ShipmentListPage : ContentPage
    {
        public ShipmentListPage()
        {
            InitializeComponent();
            BindingContext = new ShipmentListViewModel();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            ((ShipmentListViewModel)BindingContext).LoadCommand.Execute(null);
        }
    }
}
