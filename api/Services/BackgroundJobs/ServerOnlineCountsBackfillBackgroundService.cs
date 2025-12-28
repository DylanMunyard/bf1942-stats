using System.Diagnostics;
using api.ClickHouse.Interfaces;
using api.PlayerTracking;
using api.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace api.Services.BackgroundJobs;

/// <summary>
/// Backfills ServerOnlineCounts from ClickHouse to SQLite.
/// Queries minute-level data from ClickHouse and aggregates to hourly granularity.
/// </summary>
public class ServerOnlineCountsBackfillBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ServerOnlineCountsBackfillBackgroundService> logger
) : IServerOnlineCountsBackfillBackgroundService
{
    private const int DefaultDays = 60;
    private const int BatchSize = 150; // SQLite has 999 param limit, 6 params/record → max ~166

    public Task RunAsync(CancellationToken ct = default)
    {
        return RunAsync(DefaultDays, ct);
    }

    public async Task RunAsync(int days, CancellationToken ct = default)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("ServerOnlineCountsBackfill");
        activity?.SetTag("days", days);

        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("Starting ServerOnlineCounts backfill for {Days} days", days);

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();
        var clickHouseReader = scope.ServiceProvider.GetRequiredService<IClickHouseReader>();

        try
        {
            // Query ClickHouse for hourly aggregates
            var query = $@"
                SELECT
                    server_guid,
                    toStartOfHour(timestamp) as hour_timestamp,
                    game,
                    AVG(players_online) as avg_players,
                    MAX(players_online) as peak_players,
                    COUNT(*) as sample_count
                FROM server_online_counts
                WHERE timestamp >= now() - INTERVAL {days} DAY
                GROUP BY server_guid, toStartOfHour(timestamp), game
                ORDER BY hour_timestamp ASC
                FORMAT TabSeparated";

            logger.LogInformation("Querying ClickHouse for hourly aggregates...");
            var queryStopwatch = Stopwatch.StartNew();
            var result = await clickHouseReader.ExecuteQueryAsync(query);
            queryStopwatch.Stop();

            var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            logger.LogInformation("Retrieved {Count} hourly records from ClickHouse in {Duration}ms",
                lines.Length, queryStopwatch.ElapsedMilliseconds);

            if (lines.Length == 0)
            {
                logger.LogInformation("No data found in ClickHouse for the specified period");
                return;
            }

            // Parse and insert in batches
            var records = new List<HourlyAggregateRecord>();
            foreach (var line in lines)
            {
                var parts = line.Split('\t');
                if (parts.Length < 6) continue;

                if (!double.TryParse(parts[3], out var avgPlayers)) continue;
                if (!int.TryParse(parts[4], out var peakPlayers)) continue;
                if (!int.TryParse(parts[5], out var sampleCount)) continue;

                records.Add(new HourlyAggregateRecord
                {
                    ServerGuid = parts[0],
                    HourTimestamp = parts[1], // ClickHouse returns ISO format
                    Game = parts[2],
                    AvgPlayers = avgPlayers,
                    PeakPlayers = peakPlayers,
                    SampleCount = sampleCount
                });
            }

            activity?.SetTag("records_parsed", records.Count);

            // Insert in batches using raw SQL for efficiency
            var totalInserted = 0;
            var batchCount = (records.Count + BatchSize - 1) / BatchSize;

            logger.LogInformation("Inserting {Count} records in {Batches} batches", records.Count, batchCount);

            for (var batchIndex = 0; batchIndex < batchCount; batchIndex++)
            {
                if (ct.IsCancellationRequested) break;

                var batch = records
                    .Skip(batchIndex * BatchSize)
                    .Take(BatchSize)
                    .ToList();

                var batchStopwatch = Stopwatch.StartNew();
                var inserted = await InsertBatchAsync(dbContext, batch, ct);
                batchStopwatch.Stop();

                totalInserted += inserted;

                var percentComplete = ((batchIndex + 1) * 100.0) / batchCount;
                logger.LogInformation(
                    "Batch {Batch}/{Total} completed in {Duration}ms - {Inserted} records ({Percent:F1}%)",
                    batchIndex + 1, batchCount, batchStopwatch.ElapsedMilliseconds, inserted, percentComplete);
            }

            stopwatch.Stop();

            activity?.SetTag("result.total_inserted", totalInserted);
            activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);

            logger.LogInformation(
                "ServerOnlineCounts backfill completed: {TotalInserted} records in {Duration}ms",
                totalInserted, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Failed to complete ServerOnlineCounts backfill");
            throw;
        }
    }

    private static async Task<int> InsertBatchAsync(
        PlayerTrackerDbContext dbContext,
        List<HourlyAggregateRecord> batch,
        CancellationToken ct)
    {
        if (batch.Count == 0) return 0;

        // Build parameterized INSERT OR REPLACE statement
        var sql = @"INSERT OR REPLACE INTO ServerOnlineCounts
            (ServerGuid, HourTimestamp, Game, AvgPlayers, PeakPlayers, SampleCount)
            VALUES ";

        var valueClauses = new List<string>();
        var parameters = new List<object>();

        for (var i = 0; i < batch.Count; i++)
        {
            var record = batch[i];
            var baseIndex = i * 6;

            valueClauses.Add($"(@p{baseIndex}, @p{baseIndex + 1}, @p{baseIndex + 2}, @p{baseIndex + 3}, @p{baseIndex + 4}, @p{baseIndex + 5})");

            // Parse ClickHouse timestamp to NodaTime Instant format
            var hourTimestamp = ParseClickHouseTimestamp(record.HourTimestamp);

            parameters.AddRange([
                record.ServerGuid,
                hourTimestamp,
                record.Game,
                record.AvgPlayers,
                record.PeakPlayers,
                record.SampleCount
            ]);
        }

        var fullSql = sql + string.Join(", ", valueClauses);
        return await dbContext.Database.ExecuteSqlRawAsync(fullSql, parameters, ct);
    }

    private static string ParseClickHouseTimestamp(string clickHouseTimestamp)
    {
        // ClickHouse returns "2025-01-14 10:00:00" format
        // SQLite stores as ISO8601 "2025-01-14T10:00:00Z"
        if (DateTime.TryParse(clickHouseTimestamp, out var dt))
        {
            var instant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
            return NodaTime.Text.InstantPattern.ExtendedIso.Format(instant);
        }

        // Fallback: try to convert directly
        return clickHouseTimestamp.Replace(" ", "T") + "Z";
    }

    private class HourlyAggregateRecord
    {
        public string ServerGuid { get; set; } = "";
        public string HourTimestamp { get; set; } = "";
        public string Game { get; set; } = "";
        public double AvgPlayers { get; set; }
        public int PeakPlayers { get; set; }
        public int SampleCount { get; set; }
    }
}
