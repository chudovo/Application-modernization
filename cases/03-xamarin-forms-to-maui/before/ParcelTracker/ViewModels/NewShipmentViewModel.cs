using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ParcelTracker.Models;
using ParcelTracker.Views;
using Xamarin.Forms;

namespace ParcelTracker.ViewModels
{
    public class NewShipmentViewModel : BaseViewModel
    {
        private string _quoteText = "";

        public NewShipmentViewModel()
        {
            Errors = new ObservableCollection<string>();
            QuoteCommand = new Command(async () => await QuoteAsync());
            SaveCommand = new Command(async () => await SaveAsync());
        }

        public string RecipientName { get; set; }
        public string City { get; set; }
        public string PostalCode { get; set; }
        public string CountryCode { get; set; }
        public string WeightText { get; set; }
        public bool IsExpress { get; set; }

        public ObservableCollection<string> Errors { get; private set; }
        public Command QuoteCommand { get; private set; }
        public Command SaveCommand { get; private set; }

        public string QuoteText
        {
            get { return _quoteText; }
            set { _quoteText = value; OnPropertyChanged(); }
        }

        private string Service { get { return IsExpress ? "Express" : "Standard"; } }

        // The weight is typed by the user, so it is parsed with the device culture: "2,5" is valid on a German phone.
        private decimal? Weight
        {
            get
            {
                decimal value;
                return decimal.TryParse(WeightText, NumberStyles.Number, CultureInfo.CurrentCulture, out value) ? value : (decimal?)null;
            }
        }

        private bool Validate()
        {
            Errors.Clear();
            if (string.IsNullOrWhiteSpace(RecipientName)) Errors.Add("RecipientName is required.");
            if (CountryCode == null || CountryCode.Length != 2 || !CountryCode.All(char.IsLetter)) Errors.Add("CountryCode must be a two-letter ISO code.");
            if (!Weight.HasValue || Weight.Value <= 0 || Weight.Value > 30) Errors.Add("WeightKg must be greater than 0 and at most 30.");
            return Errors.Count == 0;
        }

        private async Task QuoteAsync()
        {
            QuoteText = "";
            if (!Validate()) return;
            IsBusy = true;
            try
            {
                var price = await Api.QuoteAsync(Weight.Value, CountryCode, Service);
                QuoteText = price.ToString("0.00", CultureInfo.InvariantCulture) + " EUR";
            }
            catch (ApiException ex)
            {
                foreach (var error in ex.Errors.DefaultIfEmpty(ex.Message)) Errors.Add(error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SaveAsync()
        {
            if (!Validate()) return;
            IsBusy = true;
            try
            {
                var created = await Api.CreateAsync(new NewShipment
                {
                    RecipientName = RecipientName, City = City, PostalCode = PostalCode, CountryCode = CountryCode,
                    WeightKg = Weight.Value, Service = Service
                }, Settings.Get("client.id", null));

                MessagingCenter.Send<BaseViewModel>(this, ShipmentChangedMessage);

                // Replace the form with the detail page of the new shipment.
                var form = Navigation.NavigationStack.Last();
                await Navigation.PushAsync(new ShipmentDetailPage(created.TrackingNumber));
                Navigation.RemovePage(form);
            }
            catch (ApiException ex)
            {
                foreach (var error in ex.Errors.DefaultIfEmpty(ex.Message)) Errors.Add(error);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
