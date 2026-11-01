using AX206.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VirtualKey = Windows.System.VirtualKey;

namespace AX206.Pages;

public sealed partial class SourcesPage : UserControl
{
    private readonly AppServices _services = App.Services;
    private readonly DispatcherQueueTimer _statusTimer;
    private bool _loading = true; // suppresses change handlers while the controls are filled in

    public SourcesPage()
    {
        InitializeComponent();

        var radar = _services.Settings.Current.Radar;
        LatBox.Value = radar.Lat ?? double.NaN;
        LonBox.Value = radar.Lon ?? double.NaN;
        RangeBox.Value = radar.RangeKm;
        FeedBox.SelectedItem = FeedBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == radar.Source)
                               ?? FeedBox.Items[0];
        AirportBox.Text = radar.Airport;
        RemoveTokenButton.IsEnabled = _services.HasSavedClaudeToken;
        _loading = false;

        _statusTimer = DispatcherQueue.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromSeconds(1);
        _statusTimer.Tick += (_, _) => RefreshStatus();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshStatus();
        _statusTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _statusTimer.Stop();

    private void RefreshStatus()
    {
        var data = _services.Data;

        var claude = data.Claude.Current;
        ClaudeStatus.Text = claude.Ok
            ? $"Connected · 5-hour {claude.SessionPercent:0}% · weekly {claude.WeeklyPercent:0}%"
            : claude.Error ?? "";

        var radar = data.Radar.Current;
        RadarStatus.Text = !data.Radar.Config.Enabled ? "The radar screen is off, so the feeds aren't polled."
            : radar.Ok ? $"{radar.InRange} aircraft within {radar.RangeKm:0} km · {radar.Source} · {radar.Home?.Label}"
                         + (radar.Error is null ? "" : $"\n{radar.Error}")
            : radar.Error ?? "";

        var weather = data.Weather.Current;
        WeatherStatus.Text = weather.Ok ? $"{weather.City} · {weather.Temp:0}° · {weather.Label}" : weather.Error ?? "";
    }

    // ---- Claude token -----------------------------------------------------------------------

    private void SaveToken_Click(object sender, RoutedEventArgs e) => SaveToken();

    private void TokenBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) SaveToken();
    }

    private void SaveToken()
    {
        string token = TokenBox.Password.Trim();
        if (token.Length == 0) return;
        _services.SetClaudeToken(token);
        TokenBox.Password = "";
        RemoveTokenButton.IsEnabled = true;
        ClaudeStatus.Text = "Saved. Checking…";
    }

    private void RemoveToken_Click(object sender, RoutedEventArgs e)
    {
        _services.SetClaudeToken(null);
        RemoveTokenButton.IsEnabled = false;
        ClaudeStatus.Text = "Removed. Checking ~/.claude/.credentials.json…";
    }

    // ---- Radar --------------------------------------------------------------------------------

    private void Location_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading) return;
        double? lat = double.IsNaN(LatBox.Value) ? null : LatBox.Value;
        double? lon = double.IsNaN(LonBox.Value) ? null : LonBox.Value;
        // A manual location needs both; one on its own falls back to the IP location.
        _services.UpdateRadar(r => { r.Lat = lat; r.Lon = lon; });
    }

    private void Range_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading) return;
        if (double.IsNaN(args.NewValue)) { sender.Value = args.OldValue; return; }
        _services.UpdateRadar(r => r.RangeKm = args.NewValue); // RadarSettings clamps to 2-20 km
    }

    private void Feed_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || FeedBox.SelectedItem is not ComboBoxItem { Tag: string source }) return;
        _services.UpdateRadar(r => r.Source = source);
    }

    private void Airport_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) Airport_Commit(sender, e);
    }

    private void Airport_Commit(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        string code = new(AirportBox.Text.Trim().ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
        if (code != _services.Settings.Current.Radar.Airport)
            _services.UpdateRadar(r => r.Airport = code);
    }
}
