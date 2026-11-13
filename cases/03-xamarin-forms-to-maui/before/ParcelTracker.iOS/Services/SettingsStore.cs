using Foundation;
using ParcelTracker.iOS.Services;
using ParcelTracker.Services;
using Xamarin.Forms;

[assembly: Dependency(typeof(SettingsStore))]

namespace ParcelTracker.iOS.Services
{
    public class SettingsStore : ISettingsStore
    {
        public string Get(string key, string defaultValue)
        {
            return NSUserDefaults.StandardUserDefaults.StringForKey(key) ?? defaultValue;
        }

        public void Set(string key, string value)
        {
            NSUserDefaults.StandardUserDefaults.SetString(value, key);
            NSUserDefaults.StandardUserDefaults.Synchronize();
        }
    }
}
