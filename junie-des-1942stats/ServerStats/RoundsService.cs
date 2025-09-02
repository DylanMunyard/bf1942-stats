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

    public async Task<PlayerStats.Models.PagedResult<RoundWithPlayers>> GetRounds(
        int page, 
        int pageSize, 
        string sortBy, 
        string sortOrder, 
        RoundFilters filters,
        bool includePlayers = true)
    {
        var query = _dbContext.Rounds.AsNoTracking();

        // Apply filters
        if (!string.IsNullOrWhiteSpace(filters.ServerName))
        {
            query = query.Where(r => r.ServerName.Contains(filters.ServerName));
        }

        if (!string.IsNullOrWhiteSpace(filters.ServerGuid))
        {
            query = query.Where(r => r.ServerGuid == filters.ServerGuid);
        }

        if (!string.IsNullOrWhiteSpace(filters.MapName))
        {
            query = query.Where(r => r.MapName.Contains(filters.MapName));
        }

        if (!string.IsNullOrWhiteSpace(filters.GameType))
        {
            query = query.Where(r => r.GameType == filters.GameType);
        }

        if (!string.IsNullOrWhiteSpace(filters.GameId))
        {
            query = query.Where(r => r.ServerGuid.Contains(filters.GameId));
        }

        if (filters.StartTimeFrom.HasValue)
        {
            query = query.Where(r => r.StartTime >= filters.StartTimeFrom.Value);
        }

        if (filters.StartTimeTo.HasValue)
        {
            query = query.Where(r => r.StartTime <= filters.StartTimeTo.Value);
        }

        if (filters.EndTimeFrom.HasValue)
        {
            query = query.Where(r => r.EndTime >= filters.EndTimeFrom.Value);
        }

        if (filters.EndTimeTo.HasValue)
        {
            query = query.Where(r => r.EndTime <= filters.EndTimeTo.Value);
        }

        if (filters.MinDuration.HasValue)
        {
            query = query.Where(r => r.DurationMinutes >= filters.MinDuration.Value);
        }

        if (filters.MaxDuration.HasValue)
        {
            query = query.Where(r => r.DurationMinutes <= filters.MaxDuration.Value);
        }

        if (filters.MinParticipants.HasValue)
        {
            query = query.Where(r => r.ParticipantCount >= filters.MinParticipants.Value);
        }

        if (filters.MaxParticipants.HasValue)
        {
            query = query.Where(r => r.ParticipantCount <= filters.MaxParticipants.Value);
        }

        if (filters.IsActive.HasValue)
        {
            query = query.Where(r => r.IsActive == filters.IsActive.Value);
        }

        // Apply sorting
        query = sortBy.ToLowerInvariant() switch
        {
            "roundid" => sortOrder.ToLowerInvariant() == "asc" 
                ? query.OrderBy(r => r.RoundId) 
                : query.OrderByDescending(r => r.RoundId),
            "servername" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(r => r.ServerName)
                : query.OrderByDescending(r => r.ServerName),
            "mapname" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(r => r.MapName)
                : query.OrderByDescending(r => r.MapName),
            "gametype" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(r => r.GameType)
                : query.OrderByDescending(r => r.GameType),
            "endtime" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(r => r.EndTime)
                : query.OrderByDescending(r => r.EndTime),
            "durationminutes" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(r => r.DurationMinutes)
                : query.OrderByDescending(r => r.DurationMinutes),
            "participantcount" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(r => r.ParticipantCount)
                : query.OrderByDescending(r => r.ParticipantCount),
            "isactive" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(r => r.IsActive)
                : query.OrderByDescending(r => r.IsActive),
            _ => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(r => r.StartTime)
                : query.OrderByDescending(r => r.StartTime)
        };

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply pagination
        var rounds = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Convert to RoundWithPlayers and optionally load players
        var result = new List<RoundWithPlayers>();

        foreach (var round in rounds)
        {
            var roundWithPlayers = new RoundWithPlayers
            {
                RoundId = round.RoundId,
                ServerName = round.ServerName,
                ServerGuid = round.ServerGuid,
                MapName = round.MapName,
                GameType = round.GameType,
                StartTime = round.StartTime,
                EndTime = round.EndTime ?? DateTime.UtcNow,
                DurationMinutes = round.DurationMinutes ?? 0,
                ParticipantCount = round.ParticipantCount ?? 0,
                IsActive = round.IsActive,
                Team1Label = round.Team1Label,
                Team2Label = round.Team2Label,
                Players = new List<PlayerStats.Models.SessionListItem>()
            };

            if (includePlayers)
            {
                var players = await _dbContext.PlayerSessions
                    .AsNoTracking()
                    .Where(ps => ps.RoundId == round.RoundId)
                    .OrderBy(ps => ps.PlayerName)
                    .Select(ps => new PlayerStats.Models.SessionListItem
                    {
                        SessionId = ps.SessionId,
                        RoundId = ps.RoundId,
                        PlayerName = ps.PlayerName,
                        ServerName = round.ServerName,
                        ServerGuid = ps.ServerGuid,
                        MapName = ps.MapName,
                        GameType = ps.GameType,
                        StartTime = ps.StartTime,
                        EndTime = ps.IsActive ? ps.LastSeenTime : ps.LastSeenTime,
                        DurationMinutes = (int)(ps.LastSeenTime - ps.StartTime).TotalMinutes,
                        Score = ps.TotalScore,
                        Kills = ps.TotalKills,
                        Deaths = ps.TotalDeaths,
                        IsActive = ps.IsActive
                    })
                    .ToListAsync();

                roundWithPlayers.Players = players;
            }

            result.Add(roundWithPlayers);
        }

        return new PlayerStats.Models.PagedResult<RoundWithPlayers>
        {
            Items = result,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalCount,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        };
    }
}


