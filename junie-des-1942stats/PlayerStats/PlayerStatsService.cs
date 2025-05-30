using junie_des_1942stats.PlayerStats.Models;
using Microsoft.EntityFrameworkCore;

namespace junie_des_1942stats.PlayerStats;

public class PlayerStatsService
{
    private readonly PlayerTrackerDbContext _dbContext;

    public PlayerStatsService(PlayerTrackerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // Get total play time for a player (in minutes)
    public async Task<PlayerTimeStatistics> GetPlayerStatistics(string playerName)
    {
        // Get all sessions for this player
        var sessions = await _dbContext.PlayerSessions
            .Where(ps => ps.PlayerName == playerName)
            .ToListAsync();
            
        if (!sessions.Any())
            return new PlayerTimeStatistics();
            
        var stats = new PlayerTimeStatistics
        {
            TotalSessions = sessions.Count,
            TotalPlayTimeMinutes = sessions.Sum(s => 
                (int)Math.Ceiling((s.LastSeenTime - s.StartTime).TotalMinutes)),
            TotalObservations = sessions.Sum(s => s.ObservationCount),
            FirstPlayed = sessions.Min(s => s.StartTime),
            LastPlayed = sessions.Max(s => s.LastSeenTime),
            HighestScore = sessions.Max(s => s.TotalScore),
            TotalKills = sessions.Sum(s => s.TotalKills),
            TotalDeaths = sessions.Sum(s => s.TotalDeaths)
        };
            
        return stats;
    }

    // Get server playtime statistics
    public async Task<List<ServerPlayTimeStats>> GetServerPlaytimeStats(string playerName)
    {
        var sessions = await _dbContext.PlayerSessions
            .Where(ps => ps.PlayerName == playerName)
            .Include(ps => ps.Server)
            .ToListAsync();

        var serverStats = sessions
            .GroupBy(ps => new { ServerGuid = ps.ServerGuid, ServerName = ps.Server?.Name ?? "Unknown Server" })
            .Select(g => new ServerPlayTimeStats
            {
                ServerGuid = g.Key.ServerGuid,
                ServerName = g.Key.ServerName,
                TotalSessions = g.Count(),
                TotalPlayTimeMinutes = g.Sum(s => (int)Math.Ceiling((s.LastSeenTime - s.StartTime).TotalMinutes)),
                LastPlayed = g.Max(s => s.LastSeenTime)
            })
            .OrderByDescending(s => s.TotalPlayTimeMinutes)
            .ToList();
            
        return serverStats;
    }

    // NEW METHOD: Get server activity by time period
    public async Task<ServerActivitySummary> GetServerActivityByTimePeriod(string playerName)
    {
        DateTime now = DateTime.UtcNow;
        DateTime todayStart = now.Date;
        DateTime weekStart = now.AddDays(-(int)now.DayOfWeek).Date;
        DateTime monthStart = new DateTime(now.Year, now.Month, 1);
        DateTime yearStart = new DateTime(now.Year, 1, 1);

        var sessions = await _dbContext.PlayerSessions
            .Where(ps => ps.PlayerName == playerName)
            .Include(ps => ps.Server)
            .ToListAsync();

        var result = new ServerActivitySummary
        {
            PlayerName = playerName,
            TotalPlayTimeMinutes = sessions.Sum(s => (int)Math.Ceiling((s.LastSeenTime - s.StartTime).TotalMinutes)),
            LastSeen = sessions.Any() ? sessions.Max(s => s.LastSeenTime) : DateTime.MinValue,
                
            TodayActivity = GetServerGroupingForPeriod(sessions, s => s.StartTime >= todayStart),
            ThisWeekActivity = GetServerGroupingForPeriod(sessions, s => s.StartTime >= weekStart),
            ThisMonthActivity = GetServerGroupingForPeriod(sessions, s => s.StartTime >= monthStart),
            ThisYearActivity = GetServerGroupingForPeriod(sessions, s => s.StartTime >= yearStart),
            AllTimeActivity = GetServerGroupingForPeriod(sessions, _ => true)
        };
            
        return result;
    }

    // Helper method to group server activity by time period
    private List<ServerActivityDetail> GetServerGroupingForPeriod(
        List<PlayerSession> sessions, 
        Func<PlayerSession, bool> timePeriodFilter)
    {
        return sessions
            .Where(timePeriodFilter)
            .GroupBy(s => new { ServerGuid = s.ServerGuid, ServerName = s.Server?.Name ?? "Unknown Server" })
            .Select(g => new ServerActivityDetail
            {
                ServerGuid = g.Key.ServerGuid,
                ServerName = g.Key.ServerName,
                SessionCount = g.Count(),
                PlayTimeMinutes = g.Sum(s => (int)Math.Ceiling((s.LastSeenTime - s.StartTime).TotalMinutes)),
                LastPlayed = g.Max(s => s.LastSeenTime),
                TotalKills = g.Sum(s => s.TotalKills),
                TotalDeaths = g.Sum(s => s.TotalDeaths)
            })
            .OrderByDescending(s => s.PlayTimeMinutes)
            .ToList();
    }
        
    // Get weekly playtime statistics
    public async Task<List<WeeklyPlayTimeStats>> GetWeeklyPlaytimeStats(string playerName, int weeks = 10)
    {
        // Calculate start date (going back the specified number of weeks)
        var startDate = DateTime.UtcNow.AddDays(-7 * weeks);
            
        // Get sessions in the date range
        var sessions = await _dbContext.PlayerSessions
            .Where(ps => ps.PlayerName == playerName && ps.StartTime >= startDate)
            .ToListAsync();
            
        // Group by week
        var weeklyStats = sessions
            .GroupBy(s => new 
            { 
                Year = s.StartTime.Year, 
                Week = GetIso8601WeekOfYear(s.StartTime) 
            })
            .Select(g => new WeeklyPlayTimeStats
            {
                Year = g.Key.Year,
                Week = g.Key.Week,
                WeekStart = GetStartOfWeek(new DateTime(g.Key.Year, 1, 1).AddDays((g.Key.Week - 1) * 7)),
                TotalSessions = g.Count(),
                TotalPlayTimeMinutes = g.Sum(s => 
                    (int)Math.Ceiling((s.LastSeenTime - s.StartTime).TotalMinutes)),
                TotalKills = g.Sum(s => s.TotalKills),
                TotalDeaths = g.Sum(s => s.TotalDeaths)
            })
            .OrderByDescending(w => w.Year)
            .ThenByDescending(w => w.Week)
            .ToList();
            
        return weeklyStats;
    }

    private int GetIso8601WeekOfYear(DateTime time)
    {
        // ISO 8601 week number calculation
        DayOfWeek day = System.Globalization.CultureInfo.InvariantCulture.Calendar.GetDayOfWeek(time);
        if (day >= DayOfWeek.Monday && day <= DayOfWeek.Wednesday)
        {
            time = time.AddDays(3);
        }
        return System.Globalization.CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(
            time, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
    }

    private DateTime GetStartOfWeek(DateTime dt)
    {
        int diff = (7 + (dt.DayOfWeek - DayOfWeek.Monday)) % 7;
        return dt.AddDays(-1 * diff).Date;
    }

    // Get all players with at least one session
    public async Task<List<PlayerBasicInfo>> GetAllPlayersBasicInfo()
    {
        var players = await _dbContext.PlayerSessions
            .GroupBy(ps => ps.PlayerName)
            .Select(g => new PlayerBasicInfo
            {
                PlayerName = g.Key,
                TotalPlayTimeMinutes = g.Sum(s => (int)Math.Ceiling((s.LastSeenTime - s.StartTime).TotalMinutes)),
                LastSeen = g.Max(s => s.LastSeenTime),
                TotalSessions = g.Count()
            })
            .OrderByDescending(p => p.LastSeen)
            .ToListAsync();

        return players;
    }
}
