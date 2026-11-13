using ParcelTracker.ViewModels;
using Xamarin.Forms;

namespace ParcelTracker.Views
{
    public partial class ShipmentDetailPage : ContentPage
    {
        public ShipmentDetailPage(string trackingNumber)
        {
            InitializeComponent();
            BindingContext = new ShipmentDetailViewModel(trackingNumber);
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            ((ShipmentDetailViewModel)BindingContext).LoadCommand.Execute(null);
        }
    }
}
