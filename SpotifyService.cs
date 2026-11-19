using System.Diagnostics;
using NAudio.CoreAudioApi;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace SpotifyTB;

public sealed class MediaInfo
{
    public static readonly MediaInfo None = new();

    public bool Available { get; init; }
    public bool Playing { get; init; }
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public Image? Art { get; init; }
    public float Volume { get; init; } = 1f;
    public bool Muted { get; init; }
    public TimeSpan Position { get; init; }
    public TimeSpan Duration { get; init; }
    public DateTimeOffset Stamp { get; init; }

    /// <summary>Progresso da faixa (0 a 1), estimado entre uma leitura e outra do Windows.</summary>
    public double Progress
    {
        get
        {
            if (Duration <= TimeSpan.Zero) return 0;
            var pos = Position;
            if (Playing) pos += DateTimeOffset.Now - Stamp;
            return Math.Clamp(pos.TotalSeconds / Duration.TotalSeconds, 0, 1);
        }
    }
}

/// <summary>
/// Lê e controla o Spotify (app desktop) pela API de mídia do Windows (SMTC),
/// a mesma que alimenta os controles de volume/mídia do sistema.
/// </summary>
public sealed class SpotifyService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private string _artKey = "";
    private Image? _artCache;

    private async Task EnsureAsync()
        => _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

    private GlobalSystemMediaTransportControlsSession? Find()
        => _manager?.GetSessions().FirstOrDefault(s =>
            s.SourceAppUserModelId.Contains("spotify", StringComparison.OrdinalIgnoreCase));

    public async Task<MediaInfo> GetAsync()
    {
        await EnsureAsync();
        var session = Find();
        if (session is null) return MediaInfo.None;

        var props = await session.TryGetMediaPropertiesAsync();
        var status = session.GetPlaybackInfo().PlaybackStatus;

        var key = $"{props.Artist}|{props.Title}|{props.AlbumTitle}";
        if (key != _artKey)
        {
            _artCache = await LoadArtAsync(props.Thumbnail);
            _artKey = key;
        }

        var timeline = session.GetTimelineProperties();
        var (volume, muted) = GetVolume();
        return new MediaInfo
        {
            Available = true,
            Playing = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            Title = props.Title ?? "",
            Artist = props.Artist ?? "",
            Art = _artCache,
            Volume = volume,
            Muted = muted,
            Position = timeline.Position,
            Stamp = timeline.LastUpdatedTime,
            Duration = timeline.EndTime
        };
    }

    public async Task PlayPauseAsync()
    {
        await EnsureAsync();
        if (Find() is { } s) await s.TryTogglePlayPauseAsync();
    }

    public async Task NextAsync()
    {
        await EnsureAsync();
        if (Find() is { } s) await s.TrySkipNextAsync();
    }

    public async Task PreviousAsync()
    {
        await EnsureAsync();
        if (Find() is { } s) await s.TrySkipPreviousAsync();
    }

    public async Task SeekAsync(TimeSpan position)
    {
        await EnsureAsync();
        if (Find() is { } session)
            await session.TryChangePlaybackPositionAsync(position.Ticks);
    }

    public (float Volume, bool Muted) GetVolume()
    {
        try
        {
            var ids = Process.GetProcessesByName("Spotify")
                .Select(p => (uint)p.Id)
                .ToHashSet();
            if (ids.Count == 0) return (1f, false);

            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                using var session = sessions[i];
                if (!ids.Contains((uint)session.GetProcessID)) continue;
                return (session.SimpleAudioVolume.Volume, session.SimpleAudioVolume.Mute);
            }
        }
        catch { }
        return (1f, false);
    }

    public void SetVolume(float volume)
    {
        volume = Math.Clamp(volume, 0f, 1f);
        SetSpotifySessions(s => s.SimpleAudioVolume.Volume = volume);
    }

    public void ToggleMute()
    {
        SetSpotifySessions(s => s.SimpleAudioVolume.Mute = !s.SimpleAudioVolume.Mute);
    }

    private static void SetSpotifySessions(Action<AudioSessionControl> action)
    {
        try
        {
            var ids = Process.GetProcessesByName("Spotify")
                .Select(p => (uint)p.Id)
                .ToHashSet();
            if (ids.Count == 0) return;

            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                using var session = sessions[i];
                if (ids.Contains((uint)session.GetProcessID)) action(session);
            }
        }
        catch { }
    }

    private static async Task<Image?> LoadArtAsync(IRandomAccessStreamReference? reference)
    {
        if (reference is null) return null;
        try
        {
            using var stream = await reference.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.AsStreamForRead().CopyToAsync(ms);
            ms.Position = 0;
            using var tmp = Image.FromStream(ms);
            return new Bitmap(tmp); // cópia independente do stream
        }
        catch
        {
            return null;
        }
    }
}
