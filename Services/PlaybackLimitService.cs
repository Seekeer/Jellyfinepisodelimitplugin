using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EpisodeLimit.Services;

/// <summary>
/// Counts completed items per user and stops playback once the configured limit is reached.
/// v2: per-user tracking (v1 keyed on unreliable client device IDs), stop-event
/// deduplication (the Android app reports every stop twice), accurate item names,
/// and verified enforcement with retries and loud logging.
/// </summary>
public class PlaybackLimitService : IHostedService, IDisposable
{
    private const int MaxStopAttempts = 3;
    private static readonly TimeSpan VerifyDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StaleArmExpiry = TimeSpan.FromMinutes(30);

    private readonly ISessionManager _sessionManager;
    private readonly ILogger<PlaybackLimitService> _logger;

    // One tracker per user, resolved server-side. One human, one drawer.
    private readonly ConcurrentDictionary<Guid, UserTracker> _userTrackers = new();

    public PlaybackLimitService(ISessionManager sessionManager, ILogger<PlaybackLimitService> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;

        _logger.LogInformation("Episode Limit 2.0: playback monitoring started (per-user tracking)");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;

        _logger.LogInformation("Episode Limit 2.0: service stopped");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sets the item limit for a user. Counting restarts on every new limit.
    /// </summary>
    public SessionStatus SetUserLimit(Guid userId, int limit)
    {
        var tracker = _userTrackers.GetOrAdd(userId, _ => new UserTracker());

        tracker.Limit = limit;
        tracker.IsActive = limit > 0;
        tracker.EpisodesCompleted = 0;
        tracker.StopArmed = false;
        tracker.LastCountedItemId = Guid.Empty;
        tracker.LastCountedAt = DateTime.MinValue;
        tracker.ArmedAt = DateTime.MinValue;

        _logger.LogInformation("Episode Limit: Set limit to {Limit} for user {UserId}", limit, userId);

        return ToStatus(tracker);
    }

    /// <summary>
    /// Gets the current status for a user.
    /// </summary>
    public SessionStatus GetUserStatus(Guid userId)
    {
        return _userTrackers.TryGetValue(userId, out var tracker)
            ? ToStatus(tracker)
            : new SessionStatus { Limit = 0, EpisodesPlayed = 0, IsActive = false };
    }

    /// <summary>
    /// Clears the limit for a user.
    /// </summary>
    public void ClearUserLimit(Guid userId)
    {
        if (_userTrackers.TryGetValue(userId, out var tracker))
        {
            tracker.Limit = 0;
            tracker.IsActive = false;
            tracker.EpisodesCompleted = 0;
            tracker.StopArmed = false;
            tracker.ArmedAt = DateTime.MinValue;

            _logger.LogInformation("Episode Limit: Cleared limit for user {UserId}", userId);
        }
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        var userId = e.Session.UserId;
        var itemName = e.Item?.Name ?? "Unknown";

        if (!_userTrackers.TryGetValue(userId, out var tracker) || !tracker.IsActive)
        {
            return;
        }

        _logger.LogInformation(
            "Episode Limit: Playing item {Current}/{Limit} - {ItemName}",
            tracker.EpisodesCompleted + 1, tracker.Limit, itemName);

        if (!tracker.StopArmed)
        {
            return;
        }

        // A limit was reached on the previous item. If the arm is stale
        // (nothing played for a while, e.g. the user fell asleep and the
        // client ignored our stop), discard it so it never ambushes a
        // future session.
        if (DateTime.UtcNow - tracker.ArmedAt > StaleArmExpiry)
        {
            tracker.StopArmed = false;
            tracker.ArmedAt = DateTime.MinValue;
            _logger.LogInformation("Episode Limit: Discarded stale armed stop (older than 30 minutes).");
            return;
        }

        // Enforce: stop this new session, verify it actually stopped,
        // retry if ignored. Fire and forget; never block the event thread.
        _ = EnforceStopAsync(tracker, e.Session.Id, userId, itemName);
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        var userId = e.Session.UserId;

        if (!_userTrackers.TryGetValue(userId, out var tracker) || !tracker.IsActive)
        {
            return;
        }

        if (!e.PlayedToCompletion)
        {
            return;
        }

        // The Android app reports every stop twice, milliseconds apart
        // (log evidence, 7-8 November 2026). Count one item once per window.
        var itemId = e.Item?.Id ?? Guid.Empty;
        var now = DateTime.UtcNow;
        if (tracker.LastCountedItemId == itemId && now - tracker.LastCountedAt < DedupWindow)
        {
            return;
        }

        tracker.LastCountedItemId = itemId;
        tracker.LastCountedAt = now;
        tracker.EpisodesCompleted++;

        // The name comes from the stop event itself. v1 logged a name
        // captured at playback start, which was stale or empty.
        _logger.LogInformation(
            "Episode Limit: Completed {Completed}/{Limit} - {ItemName}",
            tracker.EpisodesCompleted, tracker.Limit, e.Item?.Name ?? "Unknown");

        if (tracker.EpisodesCompleted >= tracker.Limit)
        {
            tracker.StopArmed = true;
            tracker.ArmedAt = DateTime.UtcNow;
            _logger.LogInformation(
                "Episode Limit: Reached limit of {Limit}! Stop armed for the next item.",
                tracker.Limit);
        }
    }

    private async Task EnforceStopAsync(UserTracker tracker, string sessionId, Guid userId, string itemName)
    {
        // Only one enforcement loop per user at a time.
        if (Interlocked.Exchange(ref tracker.Enforcing, 1) == 1)
        {
            return;
        }

        try
        {
            for (var attempt = 1; attempt <= MaxStopAttempts; attempt++)
            {
                try
                {
                    await _sessionManager.SendPlaystateCommand(
                        null,
                        sessionId,
                        new PlaystateRequest
                        {
                            Command = PlaystateCommand.Stop,
                            ControllingUserId = userId.ToString()
                        },
                        default).ConfigureAwait(false);

                    _logger.LogInformation(
                        "Episode Limit: Stop command sent for {ItemName} (attempt {Attempt}).",
                        itemName, attempt);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Episode Limit: Failed to send stop command (attempt {Attempt})", attempt);
                }

                await Task.Delay(VerifyDelay).ConfigureAwait(false);

                // Verify: did the client actually obey?
                var session = _sessionManager.Sessions.FirstOrDefault(s => s.Id == sessionId);
                if (session == null || session.NowPlayingItem == null)
                {
                    tracker.IsActive = false;
                    tracker.StopArmed = false;
                    tracker.EpisodesCompleted = 0;
                    _logger.LogInformation("Episode Limit: Session confirmed stopped. Sweet dreams!");
                    return;
                }

                _logger.LogWarning(
                    "Episode Limit: Client {Client} on {DeviceName} is still playing {Item} after a stop command (attempt {Attempt}). Retrying.",
                    session.Client, session.DeviceName, session.NowPlayingItem.Name ?? "unknown", attempt);
            }

            // All attempts ignored. Stay armed: the next item gets the same treatment,
            // and the log keeps the evidence (see Jellysleep issue #43).
            _logger.LogError(
                "Episode Limit: {Attempts} stop commands were ignored by this client. It does not honour server-issued stop commands (see Jellysleep issue #43). Staying armed for the next item.",
                MaxStopAttempts);
        }
        finally
        {
            tracker.Enforcing = 0;
        }
    }

    private static SessionStatus ToStatus(UserTracker tracker)
    {
        return new SessionStatus
        {
            Limit = tracker.Limit,
            EpisodesPlayed = tracker.EpisodesCompleted,
            IsActive = tracker.IsActive
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _userTrackers.Clear();
    }

    /// <summary>
    /// Tracks the limit state for one user.
    /// </summary>
    private class UserTracker
    {
        public int Limit { get; set; }
        public bool IsActive { get; set; }
        public int EpisodesCompleted { get; set; }
        public bool StopArmed { get; set; }
        public DateTime ArmedAt { get; set; }
        public Guid LastCountedItemId { get; set; }
        public DateTime LastCountedAt { get; set; }
        public int Enforcing;
    }

    /// <summary>
    /// Status returned to API callers.
    /// </summary>
    public class SessionStatus
    {
        public int Limit { get; set; }
        public int EpisodesPlayed { get; set; }
        public bool IsActive { get; set; }
    }
}
