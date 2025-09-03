using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using junie_des_1942stats.ClickHouse;
using junie_des_1942stats.PlayerTracking;

namespace junie_des_1942stats.Data.Migrations;

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
        public int BatchMinutes { get; set; } = 1440 * 7 * 2; // weekly
    }

    public class RepopulateResponse
    {
        public bool Success { get; set; }
        public int TotalRows { get; set; }
        public long DurationMs { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime ExecutedAtUtc { get; set; }
    }

    [HttpPost("repopulate")]
    public async Task<ActionResult<RepopulateResponse>> Repopulate([FromBody] RepopulateRequest request)
    {
        var started = DateTime.UtcNow;
        try
        {
            // Derive range from SQLite sessions if unspecified
            var fromUtc = request.FromUtc ?? await _db.PlayerSessions.MinAsync(ps => (DateTime?)ps.StartTime) ?? DateTime.UtcNow.AddDays(-1);
            var toUtc = request.ToUtc ?? await _db.PlayerSessions.MaxAsync(ps => (DateTime?)ps.LastSeenTime) ?? DateTime.UtcNow;

            // Load all servers once; only a few hundred total
            var serversByGuid = await _db.Servers
                .Select(s => new { s.Guid, s.Game })
                .ToDictionaryAsync(s => s.Guid, s => s.Game);

            var total = 0;
            for (var windowStart = new DateTime(fromUtc.Year, fromUtc.Month, fromUtc.Day, fromUtc.Hour, 0, 0, DateTimeKind.Utc);
                 windowStart < toUtc;
                 windowStart = windowStart.AddMinutes(request.BatchMinutes))
            {
                var windowEnd = windowStart.AddMinutes(request.BatchMinutes);
                var counts = await ComputeServerOnlineCountsAsync(_db, windowStart, windowEnd, serversByGuid);
                if (counts.Count > 0)
                {
                    await _writer.WriteServerOnlineCountsAsync(counts);
                    total += counts.Count;
                }
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
    }

    // Minimal local copy to avoid making method public elsewhere
    private static async Task<List<ServerOnlineCount>> ComputeServerOnlineCountsAsync(
        PlayerTrackerDbContext db,
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyDictionary<string, string> serversByGuid)
    {
        var overlappingRounds = await db.Rounds
            .Where(r => r.StartTime < toUtc && (r.EndTime == null || r.EndTime > fromUtc))
            .Select(r => new { r.ServerGuid, r.ServerName, r.GameType, r.MapName, r.StartTime, r.EndTime })
            .ToListAsync();

        var sessions = await db.PlayerSessions
            .Where(ps => ps.LastSeenTime >= fromUtc.AddMinutes(-60)
                         && ps.StartTime <= toUtc
                         && !ps.Player.AiBot)
            .Select(ps => new { ps.PlayerName, ps.ServerGuid, ps.StartTime, ps.LastSeenTime, ps.IsActive, ps.MapName })
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
                var activeRound = overlappingRounds.FirstOrDefault(r => r.ServerGuid == s.ServerGuid && r.StartTime <= t && (r.EndTime == null || r.EndTime > t));
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
            var roundInfo = overlappingRounds.FirstOrDefault(r => r.ServerGuid == key.serverGuid && r.MapName == key.mapName && r.StartTime <= key.minute && (r.EndTime == null || r.EndTime > key.minute));
            if (roundInfo != null)
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

        return results;
    }
}


