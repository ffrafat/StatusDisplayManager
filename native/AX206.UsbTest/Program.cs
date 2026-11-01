using System.Diagnostics;
using AX206.Core;
using AX206.Core.Data;
using AX206.Core.Rendering;
using AX206.Core.Screens;
using AX206.Core.Telemetry;
using AX206.Core.Usb;
using SkiaSharp;

// Dev tool for the native port.
//   AX206.UsbTest            open the AX206, show colour bars, time a short animation
//   AX206.UsbTest render DIR  render every screen from live data to DIR/<id>.png, plus
//                             mock states for screens that may have nothing live to show

const int W = Ax206Device.Width, H = Ax206Device.Height;

if (args.Length > 0 && args[0] == "render")
{
    string dir = args.Length > 1 ? args[1] : ".";
    Directory.CreateDirectory(dir);

    using var data = new DataHub();
    data.Log += (m, l) => Console.WriteLine($"  [{l}] {m}");
    data.Start();
    var sampler = new SystemStatsSampler();
    sampler.Sample();

    // Give the providers time for their first fetch (radar needs the IP location first).
    var wait = Stopwatch.StartNew();
    while (wait.Elapsed < TimeSpan.FromSeconds(20) &&
           (data.Weather.Current.Error == "Loading..." || data.Radar.Current.Error == "Loading..." ||
            data.Bangla.Current.Error == "Loading..." || data.Claude.Current.Error == "Loading..."))
        Thread.Sleep(250);
    Thread.Sleep(1500);

    var live = new ScreenContext
    {
        Now = DateTime.Now,
        Stats = sampler.Sample(),
        Media = data.Media.Current.Info,
        Claude = data.Claude.Current,
        Antigravity = data.Antigravity.Current,
        Bangla = data.Bangla.Current,
        Weather = data.Weather.Current,
        Radar = data.Radar.Current,
        RadarConfig = data.Radar.Config
    };

    Console.WriteLine($"live: media={live.Media?.Title ?? "none"} | claude={live.Claude?.Error ?? "ok"} | ag={live.Antigravity?.Error ?? "ok"} | " +
                      $"bangla={live.Bangla?.Error ?? "ok"} | weather={live.Weather?.Error ?? live.Weather?.Label} | radar={live.Radar?.Error ?? $"{live.Radar?.InRange} in range"}");

    using var renderer = new FrameRenderer();
    void Save(IScreen screen, ScreenContext ctx, string name)
    {
        var sw = Stopwatch.StartNew();
        renderer.Render(screen, ctx);
        renderer.SavePng(Path.Combine(dir, name + ".png"));
        Console.WriteLine($"{name,-18} {sw.ElapsedMilliseconds,4} ms  (available: {screen.IsAvailable(ctx)})");
    }

    foreach (var screen in ScreenCatalog.All()) Save(screen, live, screen.Id);

    double now = live.UnixSeconds;
    Save(new MusicScreen(), Mock(live, media: new MediaInfo(
        "Never Gonna Give You Up (Official Music Video) [4K Remaster]", "Rick Astley - Topic", "Whenever You Need Somebody", "Playing", null)), "mock-music");
    Save(new ClaudeScreen(), Mock(live, claude: new ClaudeUsage(true, null, 37, 84,
        DateTimeOffset.UtcNow.AddHours(2.4), DateTimeOffset.UtcNow.AddDays(3.2))), "mock-claude");
    Save(new AntigravityScreen(), Mock(live, ag: new AntigravityUsage(true, null,
    [
        new AntigravityGroup("Gemini Flash", new SKColor(64, 196, 255), 72, DateTimeOffset.UtcNow.AddHours(3)),
        new AntigravityGroup("Gemini Pro", new SKColor(105, 240, 174), 41, DateTimeOffset.UtcNow.AddMinutes(50)),
        new AntigravityGroup("Claude", new SKColor(255, 171, 64), 12, DateTimeOffset.UtcNow.AddDays(1.5))
    ])), "mock-antigravity");
    Save(new WeatherScreen(), Mock(live, weather: new WeatherData(true, null, "Dhaka", 22.4, 21.8, 93, 18, false,
        "Heavy rain", "rain", 25, 21)), "mock-weather-night-rain");
    Save(new WeatherScreen(), Mock(live, weather: new WeatherData(true, null, "Dhaka", 27, 30, 70, 6, false,
        "Clear sky", "sun", 31, 24)), "mock-weather-night-clear");
    foreach (var (icon, label) in new[] { ("cloud-sun", "Partly cloudy"), ("cloud", "Overcast"), ("fog", "Fog"), ("snow", "Light snow"), ("storm", "Thunderstorm") })
        Save(new WeatherScreen(), Mock(live, weather: new WeatherData(true, null, "Dhaka", 18, 16, 80, 12, true,
            label, icon, 20, 14)), $"mock-weather-{icon}");
    Save(new PetScreen(), Mock(live, stats: live.Stats! with { CpuPercent = 95, RamPercent = 88 }), "mock-pet-stressed");
    Save(new PetScreen(), Mock(live, stats: live.Stats! with { CpuPercent = 8, RamPercent = 30 }), "mock-pet-happy");
    Save(new RadarScreen(), Mock(live, radar: new RadarData(true, null, new RadarHome(23.79, 90.37, "Kafrul"), 20,
        now - 20, "adsb.lol", 0, now - 1800, [], null)), "mock-radar-empty");
    return 0;
}

static ScreenContext Mock(ScreenContext b, MediaInfo? media = null, ClaudeUsage? claude = null, AntigravityUsage? ag = null,
    WeatherData? weather = null, RadarData? radar = null, SystemStats? stats = null) => new()
{
    Now = b.Now,
    Stats = stats ?? b.Stats,
    Media = media ?? b.Media,
    Claude = claude ?? b.Claude,
    Antigravity = ag ?? b.Antigravity,
    Bangla = b.Bangla,
    Weather = weather ?? b.Weather,
    Radar = radar ?? b.Radar,
    RadarConfig = b.RadarConfig
};

static void Put(byte[] buf, int x, int y, int r, int g, int b)
{
    int rgb = ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3);
    int i = (y * W + x) * 2;
    buf[i] = (byte)(rgb >> 8);   // big-endian RGB565
    buf[i + 1] = (byte)rgb;
}

static byte[] ColourBars()
{
    var buf = new byte[Ax206Device.FrameBytes];
    (int r, int g, int b)[] bars =
    [
        (255, 255, 255), (255, 255, 0), (0, 255, 255), (0, 255, 0),
        (255, 0, 255), (255, 0, 0), (0, 0, 255), (0, 0, 0)
    ];
    for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            if (y < H * 2 / 3)
            {
                var c = bars[x * bars.Length / W];
                Put(buf, x, y, c.r, c.g, c.b);
            }
            else
            {
                int v = x * 255 / (W - 1); // grey ramp along the bottom third
                Put(buf, x, y, v, v, v);
            }
        }
    return buf;
}

static byte[] MovingBar(int frame)
{
    var buf = new byte[Ax206Device.FrameBytes];
    int barX = frame * 16 % W;
    for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            bool onBar = x >= barX && x < barX + 40;
            Put(buf, x, y, onBar ? 76 : 10, onBar ? 194 : 12, onBar ? 255 : 16);
        }
    return buf;
}

try
{
    using var lcd = Ax206Device.Open();
    Console.WriteLine($"Opened AX206 at {lcd.DevicePath}");

    var sw = Stopwatch.StartNew();
    lcd.Blit(ColourBars());
    Console.WriteLine($"Colour bars blitted, CSW OK ({sw.ElapsedMilliseconds} ms)");
    Thread.Sleep(3000);

    const int frames = 40;
    var frameData = Enumerable.Range(0, frames).Select(MovingBar).ToArray();
    sw.Restart();
    foreach (var f in frameData) lcd.Blit(f);
    double ms = sw.Elapsed.TotalMilliseconds / frames;
    Console.WriteLine($"{frames} frames: {ms:F1} ms/frame = {1000 / ms:F1} FPS max");

    lcd.Blit(ColourBars());
    Console.WriteLine("Done. Colour bars left on screen.");
    return 0;
}
catch (Exception e)
{
    Console.WriteLine($"FAILED: {e.GetType().Name}: {e.Message}");
    return 1;
}
