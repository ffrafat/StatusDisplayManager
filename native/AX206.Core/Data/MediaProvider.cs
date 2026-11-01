using SkiaSharp;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace AX206.Core.Data;

public sealed record MediaState(MediaInfo? Info);

/// <summary>
/// Now playing, straight from Windows' media session manager (what backend.py reached through
/// the winsdk package). Prefers a playing session over a paused one.
/// </summary>
public sealed class MediaProvider() : PollingProvider<MediaState>(new MediaState(null))
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private string? _artworkKey;
    private SKImage? _artwork;

    protected override TimeSpan Interval => TimeSpan.FromSeconds(1);

    protected override async Task PollAsync(CancellationToken ct)
    {
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

        GlobalSystemMediaTransportControlsSession? pick = null;
        string pickStatus = "Unknown";
        foreach (var session in _manager.GetSessions())
        {
            string status = session.GetPlaybackInfo()?.PlaybackStatus.ToString() ?? "Unknown";
            if (pick is null || (status == "Playing" && pickStatus != "Playing"))
            {
                pick = session;
                pickStatus = status;
            }
        }

        var props = pick is null ? null : await pick.TryGetMediaPropertiesAsync();
        if (props is null)
        {
            Current = new MediaState(null);
            return;
        }

        // Decode the album art only when the track changes.
        string key = $"{props.Title}\n{props.Artist}\n{props.AlbumTitle}";
        if (key != _artworkKey)
        {
            _artworkKey = key;
            _artwork = await LoadArtworkAsync(props.Thumbnail);
        }

        Current = new MediaState(new MediaInfo(
            props.Title ?? "", props.Artist ?? "", props.AlbumTitle ?? "", pickStatus, _artwork));
    }

    private static async Task<SKImage?> LoadArtworkAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null) return null;
        try
        {
            using var winrtStream = await thumbnail.OpenReadAsync();
            await using var stream = winrtStream.AsStreamForRead();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            return SKImage.FromEncodedData(buffer.ToArray());
        }
        catch (Exception)
        {
            return null; // some players hand out thumbnails that can't be read; show the placeholder
        }
    }
}
