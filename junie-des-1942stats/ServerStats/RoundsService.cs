using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.ServerStats.Models;
using Microsoft.EntityFrameworkCore;

namespace junie_des_1942stats.ServerStats;

public class RoundsService(PlayerTrackerDbContext dbContext)
{
    private readonly PlayerTrackerDbContext _dbContext = dbContext;

    public async Task<List<RoundInfo>> GetRecentRoundsAsync(string serverGuid, int limit)
    {
        var rounds = await _dbContext.Rounds
            .AsNoTracking()
            .Where(r => r.ServerGuid == serverGuid)
            .OrderByDescending(r => r.StartTime)
            .Take(limit)
            .Select(r => new RoundInfo
            {
                RoundId = r.RoundId,
                MapName = r.MapName,
                StartTime = r.StartTime,
                EndTime = r.EndTime ?? DateTime.UtcNow,
                IsActive = r.IsActive
            })
            .ToListAsync();

        return rounds;
    }
}


