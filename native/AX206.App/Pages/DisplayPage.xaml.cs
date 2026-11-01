using System.Runtime.InteropServices.WindowsRuntime;
using AX206.Core;
using AX206.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AX206.Pages;

public sealed partial class DisplayPage : UserControl
{
    private readonly AppServices _services = App.Services;
    private readonly WriteableBitmap _preview = new(480, 320);
    private bool _connected;

    public DisplayPage()
    {
        InitializeComponent();
        PreviewImage.Source = _preview;

        var display = _services.Display;
        display.FrameRendered += frame => DispatcherQueue.TryEnqueue(() => ShowFrame(frame));
        display.ConnectionChanged += connected => DispatcherQueue.TryEnqueue(() => ShowConnection(connected));
        display.RotationChanged += state => DispatcherQueue.TryEnqueue(() => ShowRotation(state));
        ShowConnection(display.IsConnected); // auto-connect may have finished before this page existed
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

        // The usual cause of a failed open is another app (the Electron version) holding the USB.
        StatusDetail.Text = connected || _services.Display.LastConnectError is null
            ? "AX206 USB display, 480 × 320"
            : "Couldn't open the display. If the Electron version is running, quit it from its tray icon, then connect.";

        ConnectButton.Content = connected ? "Disconnect" : "Connect";
        ConnectButton.Style = connected ? null : (Style)Application.Current.Resources["AccentButtonStyle"];
        ConnectButton.IsEnabled = true;
    }

    private void ShowRotation(RotationState state) =>
        RotationText.Text = state.Paused
            ? $"{state.Current}  ·  rotation paused"
            : $"{state.Current}  ·  next: {state.Next} in {state.SecondsLeft}s";

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        if (_connected) await Task.Run(_services.Display.Disconnect);
        else await Task.Run(_services.Display.Connect);
        ShowConnection(_services.Display.IsConnected);
    }

    private void Previous_Click(object sender, RoutedEventArgs e) => _services.Display.Previous();
    private void Next_Click(object sender, RoutedEventArgs e) => _services.Display.Next();

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        bool paused = _services.Display.TogglePause();
        PauseIcon.Glyph = paused ? "" : ""; // Play : Pause
        ToolTipService.SetToolTip(PauseButton, paused ? "Resume rotation" : "Pause rotation");
    }
}
