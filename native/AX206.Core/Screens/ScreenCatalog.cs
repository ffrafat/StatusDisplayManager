namespace AX206.Core.Screens;

public static class ScreenCatalog
{
    /// <summary>Every built-in screen, in rotation order (same order as the Electron app).</summary>
    public static IScreen[] All() =>
    [
        new ClockScreen(),
        new StatsScreen(),
        new MusicScreen(),
        new ClaudeScreen(),
        new AntigravityScreen(),
        new BanglaGovScreen(),
        new WeatherScreen(),
        new PetScreen(),
        new RadarScreen()
    ];
}
