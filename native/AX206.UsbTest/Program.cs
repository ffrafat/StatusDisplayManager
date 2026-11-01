using System.Diagnostics;
using AX206.Core.Rendering;
using AX206.Core.Screens;
using AX206.Core.Telemetry;
using AX206.Core.Usb;

// Dev tool for the native port.
//   AX206.UsbTest            open the AX206, show colour bars, time a short animation
//   AX206.UsbTest render DIR  render every screen to DIR/<id>.png (no device needed)

const int W = Ax206Device.Width, H = Ax206Device.Height;

if (args.Length > 0 && args[0] == "render")
{
    string dir = args.Length > 1 ? args[1] : ".";
    Directory.CreateDirectory(dir);
    var sampler = new SystemStatsSampler();
    sampler.Sample();
    Thread.Sleep(1000); // CPU % and network rates are deltas
    var context = new ScreenContext { Now = DateTime.Now, Stats = sampler.Sample() };

    using var renderer = new FrameRenderer();
    foreach (IScreen screen in new IScreen[] { new ClockScreen(), new StatsScreen() })
    {
        var sw = Stopwatch.StartNew();
        renderer.Render(screen, context);
        string path = Path.Combine(dir, screen.Id + ".png");
        renderer.SavePng(path);
        Console.WriteLine($"{screen.Id}: rendered in {sw.ElapsedMilliseconds} ms -> {path}");
    }
    return 0;
}

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
