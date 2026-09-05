using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaShelf;

public class SyncService : IHostedService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILogger<SyncService> _logger;
    private readonly MediaShelfClient _client;
    private readonly ConcurrentDictionary<Guid, DateTime> _lastProgressPush = new();

    // ItemAdded can fire before metadata providers populate ProviderIds.
    // Hold the id and wait for ItemUpdated to land before pushing to /sync/collection,
    // so the MediaShelf entry carries real TMDB/IMDb/etc. keys instead of falling back
    // to title+year. Entries older than PendingTtl are evicted by the cleanup loop.
    private readonly ConcurrentDictionary<Guid, DateTime> _pendingCollectionSync = new();
    private static readonly TimeSpan PendingTtl = TimeSpan.FromHours(1);

    public SyncService(
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        MediaShelfClient client,
        ILogger<SyncService> logger)
    {
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _logger = logger;
        _client = client;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        _libraryManager.ItemUpdated += OnItemUpdated;
        _userDataManager.UserDataSaved += OnUserDataSaved;
        _ = RunPendingCleanupAsync(cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemUpdated -= OnItemUpdated;
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        return Task.CompletedTask;
    }

    private async void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        try
        {
            var config = Plugin.Instance?.Configuration;
            if (config is null || !config.SyncCollection) return;

            var resource = ItemMapper.ResourceFor(e.Item);
            if (resource is null) return; // episodes etc. are covered via their parent series

            if (!ItemMapper.HasProviderIds(e.Item))
            {
                // Defer: provider metadata hasn't been populated yet. The first
                // ItemUpdated for this id will pick it up.
                _pendingCollectionSync[e.Item.Id] = DateTime.UtcNow;
                return;
            }

            await _client.AddToCollectionAsync(config.ServerUrl, config.ApiToken, resource, ItemMapper.ToEntry(e.Item)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MediaShelf: failed to sync new item {Name}", e.Item.Name);
        }
    }

    private async void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        try
        {
            // We only care about updates for items we deferred from ItemAdded.
            if (!_pendingCollectionSync.TryRemove(e.Item.Id, out _)) return;

            var config = Plugin.Instance?.Configuration;
            if (config is null || !config.SyncCollection) return;

            var resource = ItemMapper.ResourceFor(e.Item);
            if (resource is null) return;

            // If the metadata pass still produced no ids, drop the entry —
            // we'd rather skip than post a stub. The user can re-add by re-scanning.
            if (!ItemMapper.HasProviderIds(e.Item)) return;

            await _client.AddToCollectionAsync(config.ServerUrl, config.ApiToken, resource, ItemMapper.ToEntry(e.Item)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MediaShelf: deferred sync failed for {Name}", e.Item.Name);
        }
    }

    private async void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null) return;

        try
        {
            switch (e.Item)
            {
                case Movie movie when config.SyncPlayback:
                    await HandlePlaybackAsync(config.ServerUrl, config.ApiToken, "movies", movie, e).ConfigureAwait(false);
                    break;
                case Episode episode when config.SyncPlayback && episode.Series is not null:
                    await HandleEpisodeAsync(config.ServerUrl, config.ApiToken, episode, e).ConfigureAwait(false);
                    break;
            }

            if (config.SyncRatings
                && e.SaveReason == UserDataSaveReason.UpdateUserRating
                && e.UserData.Rating.HasValue
                && e.Item is Movie or Series)
            {
                var resource = ItemMapper.ResourceFor(e.Item);
                if (resource is not null)
                {
                    var entry = ItemMapper.ToEntry(e.Item);
                    entry["rating"] = Math.Round(e.UserData.Rating.Value / 2.0, 1); // Jellyfin 1-10 -> MediaShelf 0-5
                    await _client.RateAsync(config.ServerUrl, config.ApiToken, resource, entry).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MediaShelf: failed to sync playback for {Name}", e.Item.Name);
        }
    }

    private async Task HandlePlaybackAsync(string serverUrl, string apiToken, string resource, BaseItem item, UserDataSaveEventArgs e)
    {
        if (e.UserData.Played && e.SaveReason is UserDataSaveReason.TogglePlayed or UserDataSaveReason.PlaybackFinished)
        {
            await _client.AddHistoryAsync(serverUrl, apiToken, resource, ItemMapper.ToEntry(item)).ConfigureAwait(false);
            return;
        }

        if (e.SaveReason != UserDataSaveReason.PlaybackProgress || item.RunTimeTicks is null or 0) return;
        if (!ShouldPushProgress(item.Id)) return;

        var entry = ItemMapper.ToEntry(item);
        entry["progress"] = Math.Round(100.0 * e.UserData.PlaybackPositionTicks / item.RunTimeTicks.Value, 1);
        await _client.UpdatePlaybackAsync(serverUrl, apiToken, resource, entry).ConfigureAwait(false);
    }

    private async Task HandleEpisodeAsync(string serverUrl, string apiToken, Episode episode, UserDataSaveEventArgs e)
    {
        var series = episode.Series;
        if (series is null) return;

        if (e.UserData.Played && e.SaveReason is UserDataSaveReason.TogglePlayed or UserDataSaveReason.PlaybackFinished)
        {
            var entry = ItemMapper.ToEntry(series);
            if (episode.ParentIndexNumber.HasValue) entry["season"] = episode.ParentIndexNumber.Value;
            if (episode.IndexNumber.HasValue) entry["episode"] = episode.IndexNumber.Value;
            await _client.AddHistoryAsync(serverUrl, apiToken, "shows", entry).ConfigureAwait(false);
        }

        // Per-episode playback percentage doesn't map cleanly onto MediaShelf's
        // show-level progress field, so only the completed-episode event above
        // is synced for episodes; in-progress scrubbing is not.
    }

    private bool ShouldPushProgress(Guid itemId)
    {
        var throttle = TimeSpan.FromSeconds(Math.Max(5, Plugin.Instance?.Configuration.ProgressThrottleSeconds ?? 30));
        var now = DateTime.UtcNow;
        if (_lastProgressPush.TryGetValue(itemId, out var last) && now - last < throttle) return false;
        _lastProgressPush[itemId] = now;
        return true;
    }

    private async Task RunPendingCleanupAsync(CancellationToken cancellationToken)
    {
        // Light-touch sweep: every 10 minutes, drop pending entries older than PendingTtl
        // so a failed scan doesn't leak the dict indefinitely.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var cutoff = DateTime.UtcNow - PendingTtl;
                foreach (var kvp in _pendingCollectionSync)
                {
                    if (kvp.Value < cutoff)
                    {
                        _pendingCollectionSync.TryRemove(kvp.Key, out _);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
