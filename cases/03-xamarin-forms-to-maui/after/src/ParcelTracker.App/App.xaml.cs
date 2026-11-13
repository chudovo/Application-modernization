namespace ParcelTracker.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    // Replaces "MainPage = new NavigationPage(...)" in the constructor.
    protected override Window CreateWindow(IActivationState? activationState) => new(new AppShell());
}
