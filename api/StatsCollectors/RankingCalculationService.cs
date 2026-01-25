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
                logger.LogInformation("Starting ranking calculation for all servers");

                using (LogContext.PushProperty("operation_type", "ranking_calculation"))
                using (LogContext.PushProperty("bulk_operation", true))
                using (var scope = services.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();
                    var recalculationService = scope.ServiceProvider.GetRequiredService<IServerPlayerRankingsRecalculationService>();

                    await CalculateRankingsForAllServers(dbContext, recalculationService, stoppingToken);

                    cycleStopwatch.Stop();
                    activity?.SetTag("cycle_duration_ms", cycleStopwatch.ElapsedMilliseconds);
                    logger.LogInformation("Ranking calculation completed successfully in {DurationMs}ms", cycleStopwatch.ElapsedMilliseconds);
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
        var currentMonthString = currentMonth.ToString("00");

        using var calculateActivity = ActivitySources.RankingCalculation.StartActivity("RankingCalculation.CalculateRankingsForAllServers");
        calculateActivity?.SetTag("year", currentYear);
        calculateActivity?.SetTag("month", currentMonth);

        var servers = await dbContext.Servers.Select(s => s.Guid).ToListAsync(ct);
        logger.LogInformation("Retrieved {ServerCount} active servers for ranking calculation", servers.Count);
        calculateActivity?.SetTag("server_count", servers.Count);

        var totalRankingsInserted = 0;
        var serversProcessed = 0;
        var serversWithErrors = 0;

        foreach (var serverGuid in servers)
        {
            using var serverActivity = ActivitySources.RankingCalculation.StartActivity("RankingCalculation.ProcessServer");
            serverActivity?.SetTag("server_guid", serverGuid);

            logger.LogDebug("Processing rankings for server {ServerGuid} for {Year}-{Month}",
                serverGuid, currentYear, currentMonthString);

            try
            {
                var count = await recalculationService.RecalculateForServerAndPeriodAsync(serverGuid, currentYear, currentMonth, ct);
                totalRankingsInserted += count;
                serversProcessed++;
                serverActivity?.SetTag("rankings_inserted", count);

                if (count > 0)
                {
                    logger.LogInformation("Successfully calculated and inserted {RankingCount} rankings for server {ServerGuid}",
                        count, serverGuid);
                }
            }
            catch (Exception ex)
            {
                serversWithErrors++;
                serverActivity?.SetTag("error", ex.Message);
                serverActivity?.SetStatus(ActivityStatusCode.Error, $"Error processing server {serverGuid}: {ex.Message}");
                logger.LogError(ex, "Error calculating rankings for server {ServerGuid}", serverGuid);
            }
        }

        calculateActivity?.SetTag("total_rankings_inserted", totalRankingsInserted);
        calculateActivity?.SetTag("servers_processed", serversProcessed);
        calculateActivity?.SetTag("servers_with_errors", serversWithErrors);
    }
}
