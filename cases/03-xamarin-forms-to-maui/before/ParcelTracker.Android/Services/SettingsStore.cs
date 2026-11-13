using Android.Content;
using ParcelTracker.Droid.Services;
using ParcelTracker.Services;
using Xamarin.Forms;

[assembly: Dependency(typeof(SettingsStore))]

namespace ParcelTracker.Droid.Services
{
    public class SettingsStore : ISettingsStore
    {
        private static ISharedPreferences Preferences
        {
            get { return Android.App.Application.Context.GetSharedPreferences("parceltracker", FileCreationMode.Private); }
        }

        public string Get(string key, string defaultValue)
        {
            return Preferences.GetString(key, defaultValue);
        }

        public void Set(string key, string value)
        {
            Preferences.Edit().PutString(key, value).Apply();
        }
    }
}
