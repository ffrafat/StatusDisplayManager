using AX206.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace AX206;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, UIElement> _pages = [];

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new SizeInt32(1120, 820));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico"));
        AppWindow.Closing += OnClosing;
        Navigate("display");
    }

    // Closing hides to the tray (the display keeps updating) unless the user turned that off.
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (App.Current.IsQuitting) return;
        args.Cancel = true;

        var settings = App.Services.Settings;
        if (!settings.Current.CloseToTray)
        {
            App.Current.Quit();
            return;
        }

        sender.Hide();
        if (!settings.Current.TrayHintShown)
        {
            settings.Current.TrayHintShown = true;
            settings.Save();
            App.Current.ShowTrayHint();
        }
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string tag = args.IsSettingsSelected
            ? "settings"
            : (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "display";
        Navigate(tag);
    }

    // Pages are created once and kept, so their state survives switching back and forth.
    private void Navigate(string tag)
    {
        if (!_pages.TryGetValue(tag, out var page))
        {
            _pages[tag] = page = tag switch
            {
                "screens" => new ScreensPage(),
                "sources" => new SourcesPage(),
                "activity" => new ActivityPage(),
                "settings" => new SettingsPage(),
                _ => new DisplayPage()
            };
        }
        PageHost.Content = page;
    }
}
