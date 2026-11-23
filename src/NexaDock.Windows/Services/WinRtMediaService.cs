using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using Windows.Media.Control;

namespace NexaDock.Windows.Services;

/// <summary>
/// Reads the current media session (Spotify, browsers, Media Player, …) through Windows' System
/// Media Transport Controls and forwards play/pause/skip.
/// </summary>
public sealed class WinRtMediaService : IMediaService, IDisposable
{
    private readonly ILogger<WinRtMediaService> _logger;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private SynchronizationContext? _ui;
    private int _refreshVersion;

    public WinRtMediaService(ILogger<WinRtMediaService> logger) => _logger = logger;

    public MediaInfo? Current { get; private set; }

    public event EventHandler? Changed;

    public async Task StartAsync()
    {
        _ui = SynchronizationContext.Current;
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => AttachSession();
            AttachSession();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Media session manager unavailable.");
        }
    }

    private void AttachSession()
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnSessionChanged;
            _session.PlaybackInfoChanged -= OnSessionChanged;
            _session.TimelinePropertiesChanged -= OnSessionChanged;
        }

        _session = _manager?.GetCurrentSession();
        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnSessionChanged;
            _session.PlaybackInfoChanged += OnSessionChanged;
            _session.TimelinePropertiesChanged += OnSessionChanged;
        }

        _ = RefreshAsync();
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        MediaInfo? info = null;
        var session = _session;

        try
        {
            if (session is not null)
            {
                var props = await session.TryGetMediaPropertiesAsync();
                var playback = session.GetPlaybackInfo();
                var timeline = session.GetTimelineProperties();
                byte[]? thumb = null;

                if (props?.Thumbnail is not null)
                {
                    using var stream = await props.Thumbnail.OpenReadAsync();
                    using var managed = stream.AsStreamForRead();
                    using var ms = new MemoryStream();
                    await managed.CopyToAsync(ms);
                    thumb = ms.ToArray();
                }

                if (props is not null && !string.IsNullOrWhiteSpace(props.Title))
                {
                    var controls = playback?.Controls;
                    info = new MediaInfo(
                        props.Title,
                        props.Artist ?? string.Empty,
                        session.SourceAppUserModelId ?? string.Empty,
                        playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                        thumb,
                        timeline?.Position ?? TimeSpan.Zero,
                        timeline is null ? TimeSpan.Zero : timeline.EndTime - timeline.StartTime,
                        controls?.IsPreviousEnabled ?? false,
                        controls?.IsNextEnabled ?? false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Reading media properties failed.");
        }

        if (version != _refreshVersion)
        {
            return; // a newer refresh superseded this one
        }

        void Publish()
        {
            Current = info;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        if (_ui is not null)
        {
            _ui.Post(_ => Publish(), null);
        }
        else
        {
            Publish();
        }
    }

    public async Task PlayPauseAsync()
    {
        if (_session is not null)
        {
            await _session.TryTogglePlayPauseAsync();
        }
    }

    public async Task NextAsync()
    {
        if (_session is not null)
        {
            await _session.TrySkipNextAsync();
        }
    }

    public async Task PreviousAsync()
    {
        if (_session is not null)
        {
            await _session.TrySkipPreviousAsync();
        }
    }

    public void Dispose()
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnSessionChanged;
            _session.PlaybackInfoChanged -= OnSessionChanged;
            _session.TimelinePropertiesChanged -= OnSessionChanged;
        }
    }
}
