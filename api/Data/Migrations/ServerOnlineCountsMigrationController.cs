using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using api.ClickHouse;
using api.ClickHouse.Models;
using api.PlayerTracking;

namespace api.Data.Migrations;

[ApiController]
[Route("stats/admin/[controller]")]
public class ServerOnlineCountsMigrationController : ControllerBase
{
    private readonly ILogger<ServerOnlineCountsMigrationController> _logger;
    private readonly PlayerMetricsWriteService _writer;
    private readonly PlayerTrackerDbContext _db;

    public ServerOnlineCountsMigrationController(
        ILogger<ServerOnlineCountsMigrationController> logger,
        PlayerMetricsWriteService writer,
        PlayerTrackerDbContext db)
    {
        _logger = logger;
        _writer = writer;
        _db = db;
    }

    public class RepopulateRequest
    {
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        public int BatchMinutes { get; set; } = 1440 * 7 * 2; // fortnightly
    }

    public class RepopulateResponse
    {
        public bool Success { get; set; }
        public int TotalRows { get; set; }
        public long DurationMs { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime ExecutedAtUtc { get; set; }
    }

    // Defines a single round interval window (used for fast lookups)
    private sealed class RoundWindow
    {
        public DateTime Start { get; set; }
        public DateTime? End { get; set; }
        public string MapName { get; set; } = "";
        public string ServerName { get; set; } = "";
    }

    [HttpPost("repopulate")]
    public async Task<ActionResult<RepopulateResponse>> Repopulate([FromBody] RepopulateRequest request)
    {
        _logger.LogWarning(
            "ServerOnlineCountsMigrationController is disabled. Returning 401 Unauthorized. Request: from={From} to={To} batchMinutes={BatchMinutes}",
            request.FromUtc, request.ToUtc, request.BatchMinutes);

        return StatusCode(401, new
        {
            message = "ServerOnlineCountsMigrationController is disabled and no longer available. This endpoint has been disabled for security reasons.",
            timestamp = DateTime.UtcNow
        });

        /*
        var started = DateTime.UtcNow;
        try
        {
            // Derive range from SQLite sessions if unspecified
            var fromUtc = request.FromUtc ?? await _db.PlayerSessions.MinAsync(ps => (DateTime?)ps.StartTime) ?? DateTime.UtcNow.AddDays(-1);
            var toUtc = request.ToUtc ?? await _db.PlayerSessions.MaxAsync(ps => (DateTime?)ps.LastSeenTime) ?? DateTime.UtcNow;

            // Load all servers once; only a few hundred total
            var serversByGuid = await _db.Servers
                .AsNoTracking()
                .Select(s => new { s.Guid, s.Game })
                .ToDictionaryAsync(s => s.Guid, s => s.Game);

            var total = 0;
            var buffer = new List<ServerOnlineCount>(200_000);
            const int MaxRowsPerInsert = 200_000;
            for (var windowStart = new DateTime(fromUtc.Year, fromUtc.Month, fromUtc.Day, fromUtc.Hour, 0, 0, DateTimeKind.Utc);
                 windowStart < toUtc;
                 windowStart = windowStart.AddMinutes(request.BatchMinutes))
            {
                var windowEnd = windowStart.AddMinutes(request.BatchMinutes);
                var counts = await ComputeServerOnlineCountsAsync(_db, windowStart, windowEnd, serversByGuid);
                if (counts.Count > 0)
                {
                    // Buffer rows for larger bulk inserts
                    buffer.AddRange(counts);
                    total += counts.Count;
                    if (buffer.Count >= MaxRowsPerInsert)
                    {
                        await _writer.WriteServerOnlineCountsAsync(buffer);
                        buffer.Clear();
                    }
                }
            }

            if (buffer.Count > 0)
            {
                await _writer.WriteServerOnlineCountsAsync(buffer);
                buffer.Clear();
            }

            var ended = DateTime.UtcNow;
            return Ok(new RepopulateResponse
            {
                Success = true,
                TotalRows = total,
                DurationMs = (long)(ended - started).TotalMilliseconds,
                ExecutedAtUtc = ended
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Server online counts repopulate failed");
            var ended = DateTime.UtcNow;
            return Ok(new RepopulateResponse
            {
                Success = false,
                TotalRows = 0,
                DurationMs = (long)(ended - started).TotalMilliseconds,
                ExecutedAtUtc = ended,
                ErrorMessage = ex.Message
            });
        }
        */
    }

    // Minimal local copy to avoid making method public elsewhere
    private static async Task<List<ServerOnlineCount>> ComputeServerOnlineCountsAsync(
        PlayerTrackerDbContext db,
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyDictionary<string, string> serversByGuid)
    {
        var overlappingRounds = await db.Rounds
            .AsNoTracking()
            .Where(r => r.StartTime < toUtc && (r.EndTime == null || r.EndTime > fromUtc))
            .Select(r => new { r.ServerGuid, r.ServerName, r.GameType, r.MapName, r.StartTime, r.EndTime })
            .ToListAsync();

        // Pre-index rounds by server and sort by start time for fast lookups
        var roundsByServer = overlappingRounds
            .GroupBy(r => r.ServerGuid)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.StartTime)
                      .Select(r => new RoundWindow
                      {
                          Start = r.StartTime,
                          End = r.EndTime,
                          MapName = r.MapName,
                          ServerName = r.ServerName
                      })
                      .ToList());

        var serverGuids = roundsByServer.Keys.ToList();

        var sessions = await db.PlayerSessions
            .AsNoTracking()
            .Where(ps => ps.LastSeenTime >= fromUtc.AddMinutes(-60)
                         && ps.StartTime <= toUtc
                         && serverGuids.Contains(ps.ServerGuid)
                         && !ps.Player.AiBot)
            .Select(ps => new { ps.ServerGuid, ps.StartTime, ps.LastSeenTime })
            .ToListAsync();

        var buckets = new Dictionary<(DateTime minute, string serverGuid, string mapName), ushort>();
        foreach (var s in sessions)
        {
            var startMinute = new DateTime(Math.Max(s.StartTime.Ticks, fromUtc.Ticks), DateTimeKind.Utc);
            var endMinuteExclusive = new DateTime(Math.Min(s.LastSeenTime.Ticks, toUtc.Ticks), DateTimeKind.Utc);
            startMinute = new DateTime(startMinute.Year, startMinute.Month, startMinute.Day, startMinute.Hour, startMinute.Minute, 0, DateTimeKind.Utc);
            endMinuteExclusive = new DateTime(endMinuteExclusive.Year, endMinuteExclusive.Month, endMinuteExclusive.Day, endMinuteExclusive.Hour, endMinuteExclusive.Minute, 0, DateTimeKind.Utc);

            for (var t = startMinute; t <= endMinuteExclusive; t = t.AddMinutes(1))
            {
                if (!roundsByServer.TryGetValue(s.ServerGuid, out var serverRounds))
                {
                    continue;
                }
                var activeRound = FindActiveRound(serverRounds, t);
                if (activeRound != null)
                {
                    var key = (t, s.ServerGuid, activeRound.MapName);
                    if (!buckets.TryGetValue(key, out var count))
                    {
                        buckets[key] = 1;
                    }
                    else
                    {
                        buckets[key] = (ushort)(count + 1);
                    }
                }
            }
        }

        var results = new List<ServerOnlineCount>();
        foreach (var kvp in buckets)
        {
            var key = kvp.Key;
            var count = kvp.Value;
            if (!serversByGuid.TryGetValue(key.serverGuid, out var game)) continue;
            if (roundsByServer.TryGetValue(key.serverGuid, out var serverRounds2))
            {
                var roundInfo = FindActiveRound(serverRounds2, key.minute);
                if (roundInfo != null && roundInfo.MapName == key.mapName)
                {
                    results.Add(new ServerOnlineCount
                    {
                        Timestamp = key.minute,
                        ServerGuid = key.serverGuid,
                        ServerName = roundInfo.ServerName,
                        PlayersOnline = count,
                        MapName = key.mapName,
                        Game = game
                    });
                }
            }
        }

        return results;

        // Local types and helpers
        static RoundWindow? FindActiveRound(List<RoundWindow> rounds, DateTime t)
        {
            if (rounds.Count == 0) return null;
            int lo = 0, hi = rounds.Count - 1;
            RoundWindow? candidate = null;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                var r = rounds[mid];
                if (r.Start <= t)
                {
                    candidate = r;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            if (candidate == null) return null;
            if (candidate.End == null || candidate.End > t) return candidate;
            return null;
        }

    }
}


