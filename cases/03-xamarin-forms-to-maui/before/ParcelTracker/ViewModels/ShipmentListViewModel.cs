using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ParcelTracker.Models;
using ParcelTracker.Views;
using Xamarin.Forms;

namespace ParcelTracker.ViewModels
{
    public class ShipmentListViewModel : BaseViewModel
    {
        private const string CacheKey = "shipments.cache";

        private string _statusFilter = "All";
        private bool _isOffline;
        private string _errorMessage;

        public ShipmentListViewModel()
        {
            Items = new ObservableCollection<Shipment>();
            StatusFilters = new List<string> { "All", "Created", "InTransit", "Delivered", "Returned" };
            LoadCommand = new Command(async () => await LoadAsync());
            OpenCommand = new Command<Shipment>(async s => await Navigation.PushAsync(new ShipmentDetailPage(s.TrackingNumber)));
            NewCommand = new Command(async () => await Navigation.PushAsync(new NewShipmentPage()));

            // Any screen that changes a shipment announces it globally; the list reloads itself.
            MessagingCenter.Subscribe<BaseViewModel>(this, ShipmentChangedMessage, async sender => await LoadAsync());
        }

        public ObservableCollection<Shipment> Items { get; private set; }
        public List<string> StatusFilters { get; private set; }
        public Command LoadCommand { get; private set; }
        public Command<Shipment> OpenCommand { get; private set; }
        public Command NewCommand { get; private set; }

        public string StatusFilter
        {
            get { return _statusFilter; }
            set { _statusFilter = value; OnPropertyChanged(); LoadCommand.Execute(null); }
        }

        public bool IsOffline
        {
            get { return _isOffline; }
            set { _isOffline = value; OnPropertyChanged(); }
        }

        public string ErrorMessage
        {
            get { return _errorMessage; }
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        private async Task LoadAsync()
        {
            IsBusy = true;
            try
            {
                ErrorMessage = null;
                IsOffline = !Connectivity.IsOnline;

                List<Shipment> shipments;
                if (IsOffline)
                {
                    var cached = Settings.Get(CacheKey, null);
                    shipments = cached == null ? new List<Shipment>() : JsonConvert.DeserializeObject<List<Shipment>>(cached);
                    if (cached == null) ErrorMessage = "You are offline. No saved shipments yet.";
                    if (StatusFilter != "All") shipments = shipments.Where(s => s.Status == StatusFilter).ToList();
                }
                else
                {
                    shipments = await Api.FindAsync(StatusFilter == "All" ? null : StatusFilter);
                    if (StatusFilter == "All") Settings.Set(CacheKey, JsonConvert.SerializeObject(shipments));
                }

                // The HTTP call resumes on a thread-pool thread, so the collection is updated on the UI thread explicitly.
                Device.BeginInvokeOnMainThread(() =>
                {
                    Items.Clear();
                    foreach (var shipment in shipments) Items.Add(shipment);
                });
            }
            catch (ApiException ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
