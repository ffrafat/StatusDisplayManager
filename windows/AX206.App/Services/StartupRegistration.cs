using Microsoft.Win32;

namespace AX206.Services;

/// <summary>Launch at sign-in via HKCU\...\Run, starting hidden in the notification area.</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AX206 Display Manager (native)";

    // Run values the Electron version writes (app.setLoginItemSettings, before and after its AUMID change).
    private static readonly string[] ElectronValues = ["electron.app.AX206 Display Manager", "com.ax206.displaymanager"];

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --hidden");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Only one app can drive the display, so warn when the Electron version also starts at sign-in.</summary>
    public static bool ElectronStartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key is not null && ElectronValues.Any(v => key.GetValue(v) is string);
        }
    }
}
