using System.Reflection;
using AX206.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AX206.Pages;

public sealed partial class SettingsPage : UserControl
{
    private readonly AppServices _services = App.Services;
    private readonly bool _loading;

    public SettingsPage()
    {
        _loading = true;
        InitializeComponent();

        var s = _services.Settings.Current;
        StartupToggle.IsOn = StartupRegistration.IsEnabled; // the registry is the source of truth
        AutoConnectToggle.IsOn = s.AutoConnect;
        CloseToTrayToggle.IsOn = s.CloseToTray;
        ElectronWarning.IsOpen = StartupRegistration.ElectronStartsWithWindows;

        string version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
        AboutCard.Description = $"Native edition {version} · .NET {Environment.Version.ToString(2)} · Windows App SDK";
        _loading = false;
    }

    private void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        StartupRegistration.Set(StartupToggle.IsOn);
        _services.Settings.Current.StartWithWindows = StartupToggle.IsOn;
        _services.Settings.Save();
    }

    private void AutoConnectToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _services.Settings.Current.AutoConnect = AutoConnectToggle.IsOn;
        _services.Settings.Save();
    }

    private void CloseToTrayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _services.Settings.Current.CloseToTray = CloseToTrayToggle.IsOn;
        _services.Settings.Save();
    }
}
