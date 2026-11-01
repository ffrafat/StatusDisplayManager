using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AX206.Core.Settings;

public sealed class ScreenSetting
{
    public bool Enabled { get; set; } = true;
    public int DurationSeconds { get; set; } = 10;
}

public sealed class RadarSettings
{
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public double RangeKm { get; set; } = 100;
    public string Source { get; set; } = "auto";
    public string Airport { get; set; } = "";
}

public sealed class AppSettings
{
    public Dictionary<string, ScreenSetting> Screens { get; set; } = new();
    public RadarSettings Radar { get; set; } = new();

    /// <summary>Off by default while the Electron version still registers its own sign-in launch.</summary>
    public bool StartWithWindows { get; set; }

    public bool AutoConnect { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool TrayHintShown { get; set; }

    public ScreenSetting Screen(string id)
    {
        if (!Screens.TryGetValue(id, out var setting))
            Screens[id] = setting = new ScreenSetting();
        return setting;
    }
}

/// <summary>Settings as JSON in %LOCALAPPDATA%\AX206DisplayManager\settings.json.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly object _lock = new();

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AX206DisplayManager");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public AppSettings Current { get; }

    private SettingsStore(AppSettings current) => Current = current;

    public static SettingsStore Load()
    {
        try
        {
            if (File.Exists(FilePath) &&
                JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) is { } loaded)
                return new SettingsStore(loaded);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return new SettingsStore(new AppSettings());
    }

    public void Save()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Folder);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, Json));
            File.Move(temp, FilePath, overwrite: true); // never leave a half-written file behind
        }
    }
}

/// <summary>The Claude token, encrypted for the current Windows user with DPAPI.</summary>
public static class SecretStore
{
    private static readonly byte[] Entropy = "AX206.ClaudeToken"u8.ToArray();
    private static string TokenPath => Path.Combine(SettingsStore.Folder, "claude-token.bin");

    public static string? LoadClaudeToken()
    {
        try
        {
            if (!File.Exists(TokenPath)) return null;
            byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(TokenPath), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is IOException or CryptographicException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void SaveClaudeToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            if (File.Exists(TokenPath)) File.Delete(TokenPath);
            return;
        }
        Directory.CreateDirectory(SettingsStore.Folder);
        byte[] cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(token.Trim()), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(TokenPath, cipher);
    }
}
