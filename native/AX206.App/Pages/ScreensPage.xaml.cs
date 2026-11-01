using AX206.Services;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AX206.Pages;

/// <summary>One settings card per screen: on/off and how many seconds it stays up.</summary>
public sealed partial class ScreensPage : UserControl
{
    private static readonly Dictionary<string, (string Glyph, string Description)> Info = new()
    {
        ["clock"] = ("", "Large time and date with a seconds bar."),
        ["stats"] = ("", "CPU, memory, disk and network."),
        ["music"] = ("", "Now playing from any app. Jumps in when playback starts."),
        ["claude"] = ("", "Claude plan usage for the 5-hour and weekly limits. Needs a token in Data sources."),
        ["ag"] = ("", "Model quotas from the Antigravity IDE, while it is running."),
        ["bangla"] = ("", "Download counts for three bangla.gov.bd tools."),
        ["weather"] = ("", "Current conditions for your location, from Open-Meteo."),
        ["pet"] = ("", "Byte, a little slime whose mood follows your PC's load."),
        ["radar"] = ("", "Nearby aircraft from adsb.lol and OpenSky. Location and range are in Data sources.")
    };

    private readonly AppServices _services = App.Services;

    public ScreensPage()
    {
        InitializeComponent();
        foreach (var screen in _services.Display.Screens)
            Cards.Children.Add(CardFor(screen.Id, screen.Title));
    }

    private SettingsCard CardFor(string id, string title)
    {
        var setting = _services.Settings.Current.Screen(id);
        var (glyph, description) = Info.GetValueOrDefault(id, ("", ""));

        var duration = new NumberBox
        {
            Value = setting.DurationSeconds,
            Minimum = 2,
            Maximum = 600,
            SmallChange = 1,
            LargeChange = 10,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Width = 96,
            IsEnabled = setting.Enabled
        };
        duration.ValueChanged += (_, e) =>
        {
            if (double.IsNaN(e.NewValue)) { duration.Value = e.OldValue; return; }
            _services.SetScreenDuration(id, (int)Math.Round(e.NewValue));
        };

        var toggle = new ToggleSwitch { IsOn = setting.Enabled, MinWidth = 0 };
        toggle.Toggled += (_, _) =>
        {
            _services.SetScreenEnabled(id, toggle.IsOn);
            duration.IsEnabled = toggle.IsOn;
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(duration);
        content.Children.Add(new TextBlock
        {
            Text = "s",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        content.Children.Add(toggle);

        return new SettingsCard
        {
            Header = title,
            Description = description,
            HeaderIcon = new FontIcon { Glyph = glyph },
            Content = content
        };
    }
}
