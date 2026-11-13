using System.Globalization;
using System.Threading.Tasks;
using ParcelTracker.Models;
using Xamarin.Forms;

namespace ParcelTracker.ViewModels
{
    public class ShipmentDetailViewModel : BaseViewModel
    {
        private readonly string _trackingNumber;
        private Shipment _shipment;

        public ShipmentDetailViewModel(string trackingNumber)
        {
            _trackingNumber = trackingNumber;
            LoadCommand = new Command(async () => await LoadAsync());
            MarkInTransitCommand = new Command(async () => await ChangeStatusAsync("InTransit"));
            MarkDeliveredCommand = new Command(async () => await ChangeStatusAsync("Delivered"));
        }

        public Command LoadCommand { get; private set; }
        public Command MarkInTransitCommand { get; private set; }
        public Command MarkDeliveredCommand { get; private set; }

        public Shipment Shipment
        {
            get { return _shipment; }
            private set
            {
                _shipment = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(DeliveredText));
                OnPropertyChanged(nameof(CanMarkInTransit));
                OnPropertyChanged(nameof(CanMarkDelivered));
            }
        }

        public string Title { get { return _trackingNumber; } }
        public string StatusLabel { get { return _shipment == null ? "" : StatusText.For(_shipment.Status); } }
        public bool CanMarkInTransit { get { return _shipment != null && _shipment.Status == "Created"; } }
        public bool CanMarkDelivered { get { return _shipment != null && _shipment.Status == "InTransit"; } }

        public string DeliveredText
        {
            get
            {
                return _shipment == null || !_shipment.DeliveredUtc.HasValue
                    ? ""
                    : "Delivered " + _shipment.DeliveredUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
            }
        }

        private async Task LoadAsync()
        {
            IsBusy = true;
            try
            {
                Shipment = await Api.GetAsync(_trackingNumber);
            }
            catch (ApiException ex)
            {
                await Alert(ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ChangeStatusAsync(string status)
        {
            IsBusy = true;
            try
            {
                Shipment = await Api.UpdateStatusAsync(_trackingNumber, status);
                MessagingCenter.Send<BaseViewModel>(this, ShipmentChangedMessage);
            }
            catch (ApiException ex)
            {
                // Someone else changed the shipment in the meantime: tell the user and show the current state.
                await Alert(ex.Message);
                await LoadAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
