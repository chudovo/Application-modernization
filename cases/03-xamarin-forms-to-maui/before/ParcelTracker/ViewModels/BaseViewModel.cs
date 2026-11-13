using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ParcelTracker.Services;
using Xamarin.Forms;

namespace ParcelTracker.ViewModels
{
    public class BaseViewModel : INotifyPropertyChanged
    {
        public const string ShipmentChangedMessage = "ShipmentChanged";

        private bool _isBusy;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool IsBusy
        {
            get { return _isBusy; }
            set { _isBusy = value; OnPropertyChanged(); }
        }

        // Every view model reaches for its dependencies through the service locator.
        protected IShipmentApi Api { get { return DependencyService.Get<IShipmentApi>(); } }
        protected IConnectivityService Connectivity { get { return DependencyService.Get<IConnectivityService>(); } }
        protected ISettingsStore Settings { get { return DependencyService.Get<ISettingsStore>(); } }

        // Navigation and dialogs go through the global application object, so the view model creates pages itself.
        protected INavigation Navigation { get { return Application.Current.MainPage.Navigation; } }

        protected Task Alert(string message)
        {
            return Application.Current.MainPage.DisplayAlert("Parcel Tracker", message, "OK");
        }

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
