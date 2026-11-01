using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using AX206.Core;
using AX206.Core.Screens;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace AX206;

public sealed partial class MainWindow : Window
{
    private const int MaxLogLines = 200;

    private readonly DisplayService _display = new([new ClockScreen(), new StatsScreen()]);
    private readonly WriteableBitmap _preview = new(480, 320);
    private readonly ObservableCollection<string> _log = [];
    private bool _connected;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new SizeInt32(1120, 820));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico"));

        PreviewImage.Source = _preview;
        LogList.ItemsSource = _log;

        _display.FrameRendered += frame => DispatcherQueue.TryEnqueue(() => ShowFrame(frame));
        _display.ConnectionChanged += connected => DispatcherQueue.TryEnqueue(() => ShowConnection(connected));
        _display.RotationChanged += state => DispatcherQueue.TryEnqueue(() => ShowRotation(state));
        _display.Log += (message, level) => DispatcherQueue.TryEnqueue(() => AddLog(message, level));

        AddLog("Native AX206 Display Manager started.", LogLevel.Info);
        _display.Start();
        _ = Task.Run(_display.Connect); // opening USB can take a moment; keep the UI responsive

        Closed += (_, _) => _display.Dispose();
    }

    private void ShowFrame(byte[] bgra)
    {
        using (var pixels = _preview.PixelBuffer.AsStream())
            pixels.Write(bgra, 0, bgra.Length);
        _preview.Invalidate();
    }

    private void ShowConnection(bool connected)
    {
        _connected = connected;
        StatusDot.Fill = connected
            ? new SolidColorBrush(ColorHelper.FromArgb(255, 108, 203, 95))
            : (Brush)Application.Current.Resources["TextFillColorDisabledBrush"];
        StatusTitle.Text = connected ? "Connected" : "Disconnected";
        ConnectButton.Content = connected ? "Disconnect" : "Connect";
        ConnectButton.Style = connected ? null : (Style)Application.Current.Resources["AccentButtonStyle"];
        ConnectButton.IsEnabled = true;
    }

    private void ShowRotation(RotationState state)
    {
        RotationText.Text = state.Paused
            ? $"{state.Current}  ·  rotation paused"
            : $"{state.Current}  ·  next: {state.Next} in {state.SecondsLeft}s";
    }

    private void AddLog(string message, LogLevel level)
    {
        string tag = level switch
        {
            LogLevel.Success => "OK  ",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERR ",
            _ => "INFO"
        };
        _log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {tag}  {message}");
        while (_log.Count > MaxLogLines) _log.RemoveAt(_log.Count - 1);
    }

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        if (_connected) _ = Task.Run(_display.Disconnect);
        else _ = Task.Run(_display.Connect);
    }

    private void Previous_Click(object sender, RoutedEventArgs e) => _display.Previous();
    private void Next_Click(object sender, RoutedEventArgs e) => _display.Next();

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        bool paused = _display.TogglePause();
        PauseIcon.Glyph = paused ? "" : ""; // Play : Pause
        ToolTipService.SetToolTip(PauseButton, paused ? "Resume rotation" : "Pause rotation");
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string tag = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "display";
        DisplayPage.Visibility = tag == "display" ? Visibility.Visible : Visibility.Collapsed;
        ActivityPage.Visibility = tag == "activity" ? Visibility.Visible : Visibility.Collapsed;
    }
}
