using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using junie_des_1942stats.PlayerTracking;

namespace junie_des_1942stats.Services;

public class TournamentManagementService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TournamentManagementService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(30);

    public TournamentManagementService(
        IServiceProvider serviceProvider,
        ILogger<TournamentManagementService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Tournament Management Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessTournamentManagement();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Tournament Management Service");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Tournament Management Service stopped");
    }

    private async Task ProcessTournamentManagement()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();

        await CloseInactiveTournaments(dbContext);
        await UpdateTournamentParticipantCounts(dbContext);
        await DetectTournamentTypes(dbContext);
    }

    private async Task CloseInactiveTournaments(PlayerTrackerDbContext dbContext)
    {
        try
        {
            var inactiveTournaments = await dbContext.Tournaments
                .Where(t => t.IsActive 
                           && t.StartTime < DateTime.UtcNow.AddHours(-6) // Active for more than 6 hours
                           && !dbContext.Rounds.Any(r => r.TournamentId == t.TournamentId && r.IsActive))
                .ToListAsync();

            foreach (var tournament in inactiveTournaments)
            {
                tournament.IsActive = false;
                tournament.EndTime = DateTime.UtcNow;
                
                // Update participant count from all rounds in tournament
                var participantCount = await dbContext.TournamentRounds
                    .Where(tr => tr.TournamentId == tournament.TournamentId)
                    .Join(dbContext.PlayerSessions,
                          tr => tr.RoundId,
                          ps => ps.RoundId,
                          (tr, ps) => ps.PlayerName)
                    .Join(dbContext.Players,
                          pn => pn,
                          p => p.Name,
                          (pn, p) => new { PlayerName = pn, p.AiBot })
                    .Where(x => !x.AiBot)
                    .Select(x => x.PlayerName)
                    .Distinct()
                    .CountAsync();

                tournament.ParticipantCount = participantCount;
                
                _logger.LogInformation("TOURNAMENT: Closed inactive tournament {TournamentId} ({Name}) with {Rounds} rounds and {Participants} participants",
                    tournament.TournamentId, tournament.Name, tournament.TotalRounds, participantCount);
            }

            if (inactiveTournaments.Any())
            {
                dbContext.Tournaments.UpdateRange(inactiveTournaments);
                await dbContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing inactive tournaments");
        }
    }

    private async Task UpdateTournamentParticipantCounts(PlayerTrackerDbContext dbContext)
    {
        try
        {
            var activeTournaments = await dbContext.Tournaments
                .Where(t => t.IsActive)
                .ToListAsync();

            foreach (var tournament in activeTournaments)
            {
                var participantCount = await dbContext.TournamentRounds
                    .Where(tr => tr.TournamentId == tournament.TournamentId)
                    .Join(dbContext.PlayerSessions,
                          tr => tr.RoundId,
                          ps => ps.RoundId,
                          (tr, ps) => ps.PlayerName)
                    .Join(dbContext.Players,
                          pn => pn,
                          p => p.Name,
                          (pn, p) => new { PlayerName = pn, p.AiBot })
                    .Where(x => !x.AiBot)
                    .Select(x => x.PlayerName)
                    .Distinct()
                    .CountAsync();

                if (tournament.ParticipantCount != participantCount)
                {
                    tournament.ParticipantCount = participantCount;
                    dbContext.Tournaments.Update(tournament);
                }
            }

            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating tournament participant counts");
        }
    }

    private async Task DetectTournamentTypes(PlayerTrackerDbContext dbContext)
    {
        try
        {
            var unknownTypeTournaments = await dbContext.Tournaments
                .Where(t => t.TournamentType == "unknown" && t.ParticipantCount.HasValue)
                .ToListAsync();

            foreach (var tournament in unknownTypeTournaments)
            {
                // Simple heuristic based on participant count
                string detectedType = tournament.ParticipantCount switch
                {
                    <= 2 => "1v1",
                    <= 4 => "2v2",
                    <= 8 => "small_team",
                    <= 16 => "medium_team",
                    _ => "large_team"
                };

                if (tournament.TournamentType != detectedType)
                {
                    tournament.TournamentType = detectedType;
                    dbContext.Tournaments.Update(tournament);
                    
                    _logger.LogInformation("TOURNAMENT: Updated tournament type for {TournamentId} to '{Type}' based on {Participants} participants",
                        tournament.TournamentId, detectedType, tournament.ParticipantCount);
                }
            }

            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting tournament types");
        }
    }
}

public static class TournamentManagementServiceExtensions
{
    public static IServiceCollection AddTournamentManagement(this IServiceCollection services)
    {
        services.AddHostedService<TournamentManagementService>();
        return services;
    }
}