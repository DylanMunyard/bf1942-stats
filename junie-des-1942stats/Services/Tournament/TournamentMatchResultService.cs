using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using junie_des_1942stats.PlayerTracking;
using NodaTime;

namespace junie_des_1942stats.Services.Tournament;

public class TournamentMatchResultService : ITournamentMatchResultService
{
    private readonly PlayerTrackerDbContext _dbContext;
    private readonly ITeamMappingService _teamMappingService;
    private readonly ILogger<TournamentMatchResultService> _logger;

    public TournamentMatchResultService(
        PlayerTrackerDbContext dbContext,
        ITeamMappingService teamMappingService,
        ILogger<TournamentMatchResultService> logger)
    {
        _dbContext = dbContext;
        _teamMappingService = teamMappingService;
        _logger = logger;
    }

    public async Task<(int ResultId, string? WarningMessage)> CreateOrUpdateMatchResultAsync(
        int tournamentId,
        int matchId,
        int mapId,
        string roundId)
    {
        try
        {
            // Get the round to extract ticket information
            var round = await _dbContext.Rounds
                .FirstOrDefaultAsync(r => r.RoundId == roundId);

            if (round == null)
                return (0, $"Round {roundId} not found");

            // Get the match to extract week information
            var match = await _dbContext.TournamentMatches
                .FirstOrDefaultAsync(m => m.Id == matchId && m.TournamentId == tournamentId);

            if (match == null)
                return (0, $"Match {matchId} not found in tournament {tournamentId}");

            // Attempt to auto-detect team mapping
            var (team1Id, team2Id, warning) = await _teamMappingService.DetectTeamMappingAsync(roundId, tournamentId);

            if (warning != null)
            {
                // Team detection failed - log warning but continue with null teams
                _logger.LogWarning("Team mapping detection failed for round {RoundId}: {Warning}", roundId, warning);
            }
            else
            {
                _logger.LogInformation(
                    "Successfully detected team mapping for round {RoundId}: Team1={Team1Id}, Team2={Team2Id}",
                    roundId, team1Id, team2Id);
            }

            // Determine winning team (team with more tickets)
            int? winningTeamId = null;
            if (team1Id > 0 && team2Id > 0 && round.Tickets1.HasValue && round.Tickets2.HasValue)
            {
                if (round.Tickets1.Value > round.Tickets2.Value)
                    winningTeamId = team1Id;
                else if (round.Tickets2.Value > round.Tickets1.Value)
                    winningTeamId = team2Id;
                // If equal, winningTeamId remains null
            }

            // Check if result already exists for this map
            var existingResult = await _dbContext.TournamentMatchResults
                .FirstOrDefaultAsync(r => r.MatchId == matchId && r.MapId == mapId);

            TournamentMatchResult result;
            if (existingResult != null)
            {
                // Update existing
                existingResult.RoundId = roundId;
                existingResult.Week = match.Week;
                existingResult.Team1Id = team1Id > 0 ? team1Id : null;
                existingResult.Team2Id = team2Id > 0 ? team2Id : null;
                existingResult.WinningTeamId = winningTeamId;
                existingResult.Team1Tickets = round.Tickets1 ?? 0;
                existingResult.Team2Tickets = round.Tickets2 ?? 0;
                existingResult.UpdatedAt = SystemClock.Instance.GetCurrentInstant();

                _dbContext.TournamentMatchResults.Update(existingResult);
                result = existingResult;
            }
            else
            {
                // Create new
                result = new TournamentMatchResult
                {
                    TournamentId = tournamentId,
                    MatchId = matchId,
                    MapId = mapId,
                    RoundId = roundId,
                    Week = match.Week,
                    Team1Id = team1Id > 0 ? team1Id : null,
                    Team2Id = team2Id > 0 ? team2Id : null,
                    WinningTeamId = winningTeamId,
                    Team1Tickets = round.Tickets1 ?? 0,
                    Team2Tickets = round.Tickets2 ?? 0,
                    CreatedAt = SystemClock.Instance.GetCurrentInstant(),
                    UpdatedAt = SystemClock.Instance.GetCurrentInstant()
                };

                _dbContext.TournamentMatchResults.Add(result);
            }

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Created/updated match result for tournament {TournamentId}, match {MatchId}, map {MapId}",
                tournamentId, matchId, mapId);

            return (result.Id, warning);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error creating/updating match result for tournament {TournamentId}, match {MatchId}, map {MapId}, round {RoundId}",
                tournamentId, matchId, mapId, roundId);
            throw;
        }
    }

    public async Task<TournamentMatchResult?> GetMatchResultAsync(int resultId)
    {
        return await _dbContext.TournamentMatchResults
            .Include(mr => mr.Tournament)
            .Include(mr => mr.Match)
            .Include(mr => mr.Map)
            .Include(mr => mr.Team1)
            .Include(mr => mr.Team2)
            .Include(mr => mr.WinningTeam)
            .FirstOrDefaultAsync(mr => mr.Id == resultId);
    }

    public async Task OverrideTeamMappingAsync(int resultId, int team1Id, int team2Id)
    {
        try
        {
            var result = await _dbContext.TournamentMatchResults.FindAsync(resultId);
            if (result == null)
                throw new InvalidOperationException($"Match result {resultId} not found");

            // Validate that both teams exist in the tournament
            var team1 = await _dbContext.TournamentTeams
                .FirstOrDefaultAsync(t => t.Id == team1Id && t.TournamentId == result.TournamentId);
            var team2 = await _dbContext.TournamentTeams
                .FirstOrDefaultAsync(t => t.Id == team2Id && t.TournamentId == result.TournamentId);

            if (team1 == null || team2 == null)
                throw new InvalidOperationException("One or both teams not found in the tournament");

            // Update team mapping and recalculate winning team
            result.Team1Id = team1Id;
            result.Team2Id = team2Id;

            // Recalculate winner based on new team assignments and ticket counts
            int? winningTeamId = null;
            if (result.Team1Tickets > result.Team2Tickets)
                winningTeamId = team1Id;
            else if (result.Team2Tickets > result.Team1Tickets)
                winningTeamId = team2Id;
            // If equal, it's a tie (winningTeamId = null)

            result.WinningTeamId = winningTeamId;
            result.UpdatedAt = SystemClock.Instance.GetCurrentInstant();

            _dbContext.TournamentMatchResults.Update(result);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Overrode team mapping for result {ResultId}: Team1={Team1Id}, Team2={Team2Id}, Winner={WinningTeamId}",
                resultId, team1Id, team2Id, winningTeamId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error overriding team mapping for result {ResultId}", resultId);
            throw;
        }
    }

    public async Task DeleteMatchResultAsync(int resultId)
    {
        try
        {
            var result = await _dbContext.TournamentMatchResults.FindAsync(resultId);
            if (result == null)
                throw new InvalidOperationException($"Match result {resultId} not found");

            _dbContext.TournamentMatchResults.Remove(result);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Deleted match result {ResultId}", resultId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting match result {ResultId}", resultId);
            throw;
        }
    }

    public async Task<List<TournamentMatchResult>> GetMatchResultsAsync(
        int tournamentId,
        string? week = null,
        int page = 1,
        int pageSize = 50)
    {
        try
        {
            var query = _dbContext.TournamentMatchResults
                .Where(mr => mr.TournamentId == tournamentId)
                .Include(mr => mr.Tournament)
                .Include(mr => mr.Match)
                .Include(mr => mr.Map)
                .Include(mr => mr.Team1)
                .Include(mr => mr.Team2)
                .Include(mr => mr.WinningTeam)
                .AsQueryable();

            if (week != null)
                query = query.Where(mr => mr.Week == week);

            return await query
                .OrderBy(mr => mr.MatchId)
                .ThenBy(mr => mr.MapId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving match results for tournament {TournamentId}", tournamentId);
            throw;
        }
    }

    public async Task<int> CreateOrUpdateManualMatchResultAsync(
        int tournamentId,
        int matchId,
        int mapId,
        int team1Id,
        int team2Id,
        int team1Tickets,
        int team2Tickets,
        int? winningTeamId = null)
    {
        try
        {
            // Validate that teams exist in the tournament
            var team1 = await _dbContext.TournamentTeams
                .FirstOrDefaultAsync(t => t.Id == team1Id && t.TournamentId == tournamentId);
            var team2 = await _dbContext.TournamentTeams
                .FirstOrDefaultAsync(t => t.Id == team2Id && t.TournamentId == tournamentId);

            if (team1 == null || team2 == null)
                throw new InvalidOperationException("One or both teams not found in the tournament");

            // Get the match to extract week information
            var match = await _dbContext.TournamentMatches
                .FirstOrDefaultAsync(m => m.Id == matchId && m.TournamentId == tournamentId);

            if (match == null)
                throw new InvalidOperationException($"Match {matchId} not found in tournament {tournamentId}");

            // Validate winning team if provided
            if (winningTeamId.HasValue && winningTeamId > 0)
            {
                if (winningTeamId != team1Id && winningTeamId != team2Id)
                    throw new InvalidOperationException("Winning team must be one of the two teams in the match");
            }

            // Check if result already exists for this map
            var existingResult = await _dbContext.TournamentMatchResults
                .FirstOrDefaultAsync(r => r.MatchId == matchId && r.MapId == mapId);

            TournamentMatchResult result;
            if (existingResult != null)
            {
                // Update existing
                existingResult.Week = match.Week;
                existingResult.Team1Id = team1Id;
                existingResult.Team2Id = team2Id;
                existingResult.WinningTeamId = winningTeamId;
                existingResult.Team1Tickets = team1Tickets;
                existingResult.Team2Tickets = team2Tickets;
                existingResult.UpdatedAt = SystemClock.Instance.GetCurrentInstant();

                _dbContext.TournamentMatchResults.Update(existingResult);
                result = existingResult;

                _logger.LogInformation(
                    "Updated manual match result {ResultId} for tournament {TournamentId}, match {MatchId}, map {MapId}",
                    existingResult.Id, tournamentId, matchId, mapId);
            }
            else
            {
                // Create new (RoundId remains null for manual entries)
                result = new TournamentMatchResult
                {
                    TournamentId = tournamentId,
                    MatchId = matchId,
                    MapId = mapId,
                    RoundId = null, // Manual entry - no round linked
                    Week = match.Week,
                    Team1Id = team1Id,
                    Team2Id = team2Id,
                    WinningTeamId = winningTeamId,
                    Team1Tickets = team1Tickets,
                    Team2Tickets = team2Tickets,
                    CreatedAt = SystemClock.Instance.GetCurrentInstant(),
                    UpdatedAt = SystemClock.Instance.GetCurrentInstant()
                };

                _dbContext.TournamentMatchResults.Add(result);

                _logger.LogInformation(
                    "Created manual match result for tournament {TournamentId}, match {MatchId}, map {MapId}",
                    tournamentId, matchId, mapId);
            }

            await _dbContext.SaveChangesAsync();

            return result.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error creating/updating manual match result for tournament {TournamentId}, match {MatchId}, map {MapId}",
                tournamentId, matchId, mapId);
            throw;
        }
    }
}
