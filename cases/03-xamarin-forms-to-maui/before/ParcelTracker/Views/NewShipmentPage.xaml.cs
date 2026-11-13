using ParcelTracker.ViewModels;
using Xamarin.Forms;

namespace ParcelTracker.Views
{
    public partial class NewShipmentPage : ContentPage
    {
        public NewShipmentPage()
        {
            InitializeComponent();
            BindingContext = new NewShipmentViewModel();
        }
    }
}
