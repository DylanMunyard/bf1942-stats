using api.PlayerTracking;
using api.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace api.StatsCollectors;

public class RankingCalculationService(IServiceProvider services, ILogger<RankingCalculationService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("RankingCalculationService started, waiting {Delay} before first run", StartupDelay);

        // Delay startup to avoid blocking Kestrel initialization
        await Task.Delay(StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var activity = ActivitySources.RankingCalculation.StartActivity("RankingCalculation.Cycle");
            activity?.SetTag("bulk_operation", "true");

            var cycleStopwatch = Stopwatch.StartNew();
            try
            {
                using (LogContext.PushProperty("bulk_operation", true))
                using (var scope = services.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();
                    var recalculationService = scope.ServiceProvider.GetRequiredService<IServerPlayerRankingsRecalculationService>();

                    await CalculateRankingsForAllServers(dbContext, recalculationService, stoppingToken);

                    cycleStopwatch.Stop();
                    activity?.SetTag("cycle_duration_ms", cycleStopwatch.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                cycleStopwatch.Stop();
                activity?.SetTag("cycle_duration_ms", cycleStopwatch.ElapsedMilliseconds);
                activity?.SetTag("error", ex.Message);
                activity?.SetStatus(ActivityStatusCode.Error, $"Ranking calculation failed: {ex.Message}");
                logger.LogError(ex, "Error calculating rankings");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task CalculateRankingsForAllServers(
        PlayerTrackerDbContext dbContext,
        IServerPlayerRankingsRecalculationService recalculationService,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var currentYear = now.Year;
        var currentMonth = now.Month;

        var servers = await dbContext.Servers.Select(s => s.Guid).ToListAsync(ct);

        var totalRankingsInserted = 0;
        var serversProcessed = 0;
        var serversWithData = 0;
        var serversWithErrors = 0;

        foreach (var serverGuid in servers)
        {
            try
            {
                var count = await recalculationService.RecalculateForServerAndPeriodAsync(serverGuid, currentYear, currentMonth, ct);
                totalRankingsInserted += count;
                serversProcessed++;
                if (count > 0) serversWithData++;
            }
            catch (Exception ex)
            {
                serversWithErrors++;
                logger.LogError(ex, "Error calculating rankings for server {ServerGuid}", serverGuid);
            }
        }

        logger.LogInformation(
            "Ranking calculation: {TotalRankings} rankings across {ServersWithData}/{TotalServers} servers for {Year}-{Month:00}",
            totalRankingsInserted, serversWithData, servers.Count, currentYear, currentMonth);
    }
}
