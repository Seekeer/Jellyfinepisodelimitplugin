using System;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Security.Claims;
using Jellyfin.Plugin.EpisodeLimit.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EpisodeLimit.Api;

/// <summary>
/// API controller for Episode Limit. v2 keys everything on the authenticated
/// user, resolved server-side, so no client-supplied device ID can ever split
/// the limit and the counter into different drawers (the v1 failure mode).
/// </summary>
[ApiController]
[Route("EpisodeLimit")]
[Authorize]
[Produces(MediaTypeNames.Application.Json)]
public class EpisodeLimitController : ControllerBase
{
    private readonly PlaybackLimitService _playbackLimitService;
    private readonly ILogger<EpisodeLimitController> _logger;

    public EpisodeLimitController(PlaybackLimitService playbackLimitService, ILogger<EpisodeLimitController> logger)
    {
        _playbackLimitService = playbackLimitService;
        _logger = logger;
    }

    /// <summary>
    /// Sets the item limit for the authenticated user.
    /// </summary>
    [HttpPost("SetLimit")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<EpisodeLimitResponse> SetLimit([FromBody] SetLimitRequest request)
    {
        if (request.Limit < 0 || request.Limit > 100)
        {
            return BadRequest("Limit must be between 0 and 100 (0 = disabled)");
        }

        if (!TryGetUserId(out var userId))
        {
            return Problem("Episode Limit 2.0 could not resolve your user from the authentication context. See the server log.");
        }

        var result = _playbackLimitService.SetUserLimit(userId, request.Limit);

        return Ok(new EpisodeLimitResponse
        {
            Limit = result.Limit,
            EpisodesPlayed = result.EpisodesPlayed,
            IsActive = result.IsActive,
            LimitStoppedSecondsAgo = result.LimitStoppedSecondsAgo
        });
    }

    /// <summary>
    /// Gets the current limit status for the authenticated user.
    /// </summary>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<EpisodeLimitResponse> GetStatus()
    {
        if (!TryGetUserId(out var userId))
        {
            return Problem("Episode Limit 2.0 could not resolve your user from the authentication context. See the server log.");
        }

        var result = _playbackLimitService.GetUserStatus(userId);

        return Ok(new EpisodeLimitResponse
        {
            Limit = result.Limit,
            EpisodesPlayed = result.EpisodesPlayed,
            IsActive = result.IsActive,
            LimitStoppedSecondsAgo = result.LimitStoppedSecondsAgo
        });
    }

    /// <summary>
    /// Clears the limit for the authenticated user.
    /// </summary>
    [HttpPost("Clear")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<EpisodeLimitResponse> ClearLimit()
    {
        if (!TryGetUserId(out var userId))
        {
            return Problem("Episode Limit 2.0 could not resolve your user from the authentication context. See the server log.");
        }

        _playbackLimitService.ClearUserLimit(userId);

        return Ok(new EpisodeLimitResponse
        {
            Limit = 0,
            EpisodesPlayed = 0,
            IsActive = false
        });
    }

    /// <summary>
    /// Resolves the user id from the authenticated claims, the same claims
    /// Jellyfin's own API uses (NameIdentifier, then JWT "sub", then the
    /// Jellyfin-UserId claim). Never trusts a client-supplied key.
    /// </summary>
    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("Jellyfin-UserId")?.Value;

        if (Guid.TryParse(value, out userId))
        {
            return true;
        }

        _logger.LogError(
            "Episode Limit 2.0: could not resolve a user id from the authentication context (value found: {Value}). " +
            "Limit tracking is disabled until this is resolved.",
            string.IsNullOrEmpty(value) ? "none" : value);
        return false;
    }
}

/// <summary>
/// Request to set the item limit.
/// </summary>
public class SetLimitRequest
{
    /// <summary>
    /// Number of items before stopping (0 = disabled, 1-100 = active).
    /// </summary>
    [Range(0, 100)]
    public int Limit { get; set; }
}

/// <summary>
/// Response with current limit status.
/// </summary>
public class EpisodeLimitResponse
{
    /// <summary>The configured limit (0 = disabled).</summary>
    public int Limit { get; set; }

    /// <summary>Items completed so far in this run.</summary>
    public int EpisodesPlayed { get; set; }

    /// <summary>Whether the limit is currently active.</summary>
    public bool IsActive { get; set; }

    /// <summary>Seconds since playback was last stopped by the limit; null if never.
    /// The player script uses it to show the "The End" screen after a limit stop.</summary>
    public int? LimitStoppedSecondsAgo { get; set; }
}
