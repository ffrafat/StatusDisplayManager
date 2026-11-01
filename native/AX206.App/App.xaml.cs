using AX206.Services;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AX206;

public partial class App : Application
{
    private SingleInstance? _instance;
    private MainWindow? _window;
    private TaskbarIcon? _tray;
    private DispatcherQueue? _ui;

    /// <summary>Data feeds, display loop and settings: they run whether or not the window is open.</summary>
    public static AppServices Services { get; private set; } = null!;

    public static new App Current => (App)Application.Current;

    public bool IsQuitting { get; private set; }

    public App()
    {
        InitializeComponent();
        // Keep running in the tray when the window is hidden or never shown (--hidden at sign-in).
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _instance = new SingleInstance("AX206DisplayManager.Native");
        if (!_instance.IsFirst)
        {
            _instance.SignalFirstInstance(); // the running copy shows its window instead
            _instance.Dispose();
            Exit();
            return;
        }

        _ui = DispatcherQueue.GetForCurrentThread();
        Services = new AppServices();
        Services.Start();

        _window = new MainWindow();
        CreateTray();
        _instance.ShowRequested += () => _ui.TryEnqueue(ShowWindow);

        if (!Environment.GetCommandLineArgs().Contains("--hidden"))
            ShowWindow();
    }

    public void ShowWindow()
    {
        if (_window is null || IsQuitting) return;
        if (_window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        _window.AppWindow.Show();
        _window.Activate();
    }

    private void CreateTray()
    {
        void OnUi(Action action) => _ui?.TryEnqueue(() => action());

        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Open AX206 Display Manager", Command = new RelayCommand(() => OnUi(ShowWindow)) });
        menu.Items.Add(new MenuFlyoutItem { Text = "Next screen", Command = new RelayCommand(Services.Display.Next) });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "Quit", Command = new RelayCommand(() => OnUi(Quit)) });

        _tray = new TaskbarIcon
        {
            ToolTipText = "AX206 Display Manager",
            IconSource = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico"))),
            ContextMenuMode = ContextMenuMode.PopupMenu, // native Win32 menu, which Windows 11 styles itself
            ContextFlyout = menu,
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(() => OnUi(ShowWindow))
        };
        // No efficiency mode: Windows would throttle the process and stall the 1 FPS display loop.
        _tray.ForceCreate(enablesEfficiencyMode: false);
    }

    /// <summary>One-time hint the first time the window closes to the tray.</summary>
    public void ShowTrayHint() =>
        _tray?.ShowNotification("Still running", "AX206 Display Manager keeps driving the display from the notification area. Right-click the icon to quit.");

    public void Quit()
    {
        if (IsQuitting) return;
        IsQuitting = true;
        _tray?.Dispose();
        Services.Dispose(); // stops the loop and releases the USB interface
        _window?.Close();
        _instance?.Dispose();
        Exit();
    }
}
