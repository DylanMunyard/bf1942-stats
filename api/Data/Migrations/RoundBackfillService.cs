using System.Security.Cryptography;
using System.Text;
using junie_des_1942stats.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Data.Migrations;

public class RoundBackfillService
{
    private readonly PlayerTrackerDbContext _dbContext;
    private readonly ILogger<RoundBackfillService> _logger;

    public RoundBackfillService(PlayerTrackerDbContext dbContext, ILogger<RoundBackfillService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<int> BackfillRoundsAsync(
        DateTime? startTimeUtc = null,
        DateTime? endTimeUtc = null,
        string? serverGuid = null,
        bool markLatestPerServerActive = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("BackfillRoundsAsync started: startTimeUtc={StartTimeUtc}, endTimeUtc={EndTimeUtc}, serverGuid={ServerGuid}",
            startTimeUtc, endTimeUtc, serverGuid ?? "ALL");

        var startTime = DateTime.UtcNow;

        // Set a longer command timeout for bulk operations
        _dbContext.Database.SetCommandTimeout(300); // 5 minute timeout for bulk operations
        _logger.LogInformation("BackfillRoundsAsync: Set command timeout to 300 seconds for bulk operations");

        // Apply a guard band of +/- 600 seconds (10 minutes) to avoid cutting rounds at boundaries
        var guardBand = TimeSpan.FromSeconds(600);
        var expandedFrom = startTimeUtc.HasValue ? startTimeUtc.Value - guardBand : (DateTime?)null;
        var expandedTo = endTimeUtc.HasValue ? endTimeUtc.Value + guardBand : (DateTime?)null;

        _logger.LogInformation("BackfillRoundsAsync: Applied guard band. expandedFrom={ExpandedFrom}, expandedTo={ExpandedTo}",
            expandedFrom, expandedTo);

        // Build base filter for pairs discovery
        // Use intersection semantics so sessions that overlap the window are included
        var baseQuery = _dbContext.PlayerSessions.AsNoTracking().AsQueryable();
        if (expandedFrom.HasValue)
        {
            baseQuery = baseQuery.Where(s => s.LastSeenTime >= expandedFrom.Value);
        }
        if (expandedTo.HasValue)
        {
            baseQuery = baseQuery.Where(s => s.StartTime <= expandedTo.Value);
        }
        if (!string.IsNullOrWhiteSpace(serverGuid))
        {
            baseQuery = baseQuery.Where(s => s.ServerGuid == serverGuid);
        }

        _logger.LogInformation("BackfillRoundsAsync: Executing pairs query...");
        var pairs = await baseQuery
            .Select(s => new { s.ServerGuid, s.MapName })
            .Distinct()
            .ToListAsync(cancellationToken);

        _logger.LogInformation("BackfillRoundsAsync: Found {PairCount} unique server/map pairs to process", pairs.Count);

        int createdOrUpdatedRounds = 0;

        // Collect all rounds to be created/updated
        var roundsToProcess = new List<(string RoundId, string ServerGuid, string MapName, DateTime GroupStart, DateTime GroupEnd, string GameType, List<int> SessionIds)>();
        var serverNames = new Dictionary<string, string>();

        foreach (var pair in pairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var serverGuidKey = pair.ServerGuid;
            var mapNameKey = pair.MapName ?? "";

            _logger.LogInformation("Backfill: processing server={ServerGuid} map={MapName}", serverGuidKey, mapNameKey);

            // Load sessions for this pair ordered by StartTime
            _logger.LogInformation("Backfill: Loading sessions for server={ServerGuid} map={MapName}...", serverGuidKey, mapNameKey);
            var list = await _dbContext.PlayerSessions.AsNoTracking()
                .Where(s => s.ServerGuid == serverGuidKey && s.MapName == mapNameKey)
                // Intersection semantics: session overlaps the [expandedFrom, expandedTo] window
                .Where(s => !expandedFrom.HasValue || s.LastSeenTime >= expandedFrom.Value)
                .Where(s => !expandedTo.HasValue || s.StartTime <= expandedTo.Value)
                .Select(s => new { s.SessionId, s.StartTime, s.LastSeenTime, s.GameType })
                .OrderBy(s => s.StartTime)
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Backfill: Loaded {SessionCount} sessions for server={ServerGuid} map={MapName}",
                list.Count, serverGuidKey, mapNameKey);

            if (list.Count == 0)
            {
                _logger.LogInformation("Backfill: No sessions found for server={ServerGuid} map={MapName}, skipping", serverGuidKey, mapNameKey);
                continue;
            }

            var currentGroup = new List<int>();
            DateTime groupStart = list[0].StartTime;
            DateTime groupEnd = list[0].LastSeenTime;
            var gameType = list[0].GameType ?? "";

            void CollectGroup()
            {
                if (currentGroup.Count == 0)
                {
                    _logger.LogDebug("CollectGroup: Empty group, skipping");
                    return;
                }

                var roundId = ComputeRoundId(serverGuidKey, mapNameKey, groupStart.ToUniversalTime());
                _logger.LogInformation("CollectGroup: Collecting group with {SessionCount} sessions, roundId={RoundId}, groupStart={GroupStart}, groupEnd={GroupEnd}",
                    currentGroup.Count, roundId, groupStart, groupEnd);

                // Skip groups that do not intersect the requested [startTimeUtc, endTimeUtc] window when a window is provided
                if (startTimeUtc.HasValue || endTimeUtc.HasValue)
                {
                    var intersects = (!startTimeUtc.HasValue || groupEnd >= startTimeUtc.Value)
                                     && (!endTimeUtc.HasValue || groupStart <= endTimeUtc.Value);
                    if (!intersects)
                    {
                        _logger.LogInformation("CollectGroup: Group does not intersect requested time window, skipping. groupStart={GroupStart}, groupEnd={GroupEnd}, startTimeUtc={StartTimeUtc}, endTimeUtc={EndTimeUtc}",
                            groupStart, groupEnd, startTimeUtc, endTimeUtc);
                        currentGroup.Clear();
                        return;
                    }
                }

                roundsToProcess.Add((roundId, serverGuidKey, mapNameKey, groupStart, groupEnd, gameType, new List<int>(currentGroup)));
                createdOrUpdatedRounds++;
                currentGroup.Clear();
            }

            currentGroup.Add(list[0].SessionId);
            _logger.LogInformation("Backfill: Starting session grouping for server={ServerGuid} map={MapName}. First session: SessionId={FirstSessionId}, StartTime={FirstStartTime}",
                serverGuidKey, mapNameKey, list[0].SessionId, list[0].StartTime);

            for (int i = 1; i < list.Count; i++)
            {
                var curr = list[i];
                // Split groups based on the gap from the current group's end time
                var gapSeconds = (curr.StartTime - groupEnd).TotalSeconds;

                if (gapSeconds > 600)
                {
                    _logger.LogInformation("Backfill: Gap of {GapSeconds} seconds from groupEnd detected before session {CurrSessionId}. Collecting group with {GroupSize} sessions.",
                        gapSeconds, curr.SessionId, currentGroup.Count);
                    CollectGroup();
                    currentGroup.Add(curr.SessionId);
                    groupStart = curr.StartTime;
                    groupEnd = curr.LastSeenTime;
                    gameType = curr.GameType ?? gameType;
                    _logger.LogInformation("Backfill: Started new group with session {SessionId}, groupStart={GroupStart}, groupEnd={GroupEnd}",
                        curr.SessionId, groupStart, groupEnd);
                }
                else
                {
                    currentGroup.Add(curr.SessionId);
                    if (curr.LastSeenTime > groupEnd)
                    {
                        _logger.LogDebug("Backfill: Extended group end time from {OldGroupEnd} to {NewGroupEnd} for session {SessionId}",
                            groupEnd, curr.LastSeenTime, curr.SessionId);
                        groupEnd = curr.LastSeenTime;
                    }
                    if (!string.IsNullOrEmpty(curr.GameType))
                    {
                        _logger.LogDebug("Backfill: Updated game type from '{OldGameType}' to '{NewGameType}' for session {SessionId}",
                            gameType, curr.GameType, curr.SessionId);
                        gameType = curr.GameType;
                    }
                }
            }

            _logger.LogInformation("Backfill: Collecting final group with {GroupSize} sessions for server={ServerGuid} map={MapName}",
                currentGroup.Count, serverGuidKey, mapNameKey);
            CollectGroup();
        }

        _logger.LogInformation("Backfill: Collected {RoundCount} rounds to process", roundsToProcess.Count);

        // Get server names for all unique servers
        var uniqueServerGuids = roundsToProcess.Select(r => r.ServerGuid).Distinct().ToList();
        _logger.LogInformation("Backfill: Loading server names for {ServerCount} unique servers", uniqueServerGuids.Count);

        var serverNamesList = await _dbContext.Servers
            .Where(s => uniqueServerGuids.Contains(s.Guid))
            .Select(s => new { s.Guid, s.Name })
            .ToListAsync(cancellationToken);

        foreach (var server in serverNamesList)
        {
            serverNames[server.Guid] = server.Name;
        }
        _logger.LogInformation("Backfill: Loaded server names for {ServerCount} servers", serverNamesList.Count);

        // Process rounds in batches
        const int roundBatchSize = 200;
        for (int batchIndex = 0; batchIndex < roundsToProcess.Count; batchIndex += roundBatchSize)
        {
            var roundBatch = roundsToProcess.Skip(batchIndex).Take(roundBatchSize).ToList();
            _logger.LogInformation("Backfill: Processing round batch {BatchNumber}/{TotalBatches} with {RoundCount} rounds",
                (batchIndex / roundBatchSize) + 1, (int)Math.Ceiling((double)roundsToProcess.Count / roundBatchSize), roundBatch.Count);

            await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // Get existing rounds for this batch
                var roundIds = roundBatch.Select(r => r.RoundId).ToList();
                var existingRounds = await _dbContext.Rounds
                    .Where(r => roundIds.Contains(r.RoundId))
                    .ToDictionaryAsync(r => r.RoundId, cancellationToken);

                _logger.LogInformation("Backfill: Found {ExistingCount} existing rounds out of {TotalCount} in batch",
                    existingRounds.Count, roundBatch.Count);

                // Create/update rounds
                var roundsToAdd = new List<Round>();
                var roundsToUpdate = new List<Round>();

                foreach (var (roundId, roundServerGuid, mapName, groupStart, groupEnd, gameType, sessionIds) in roundBatch)
                {
                    if (existingRounds.TryGetValue(roundId, out var existingRound))
                    {
                        // Update existing round
                        existingRound.ServerGuid = roundServerGuid;
                        existingRound.MapName = mapName;
                        existingRound.GameType = gameType;
                        existingRound.StartTime = groupStart;
                        existingRound.EndTime = groupEnd;
                        existingRound.IsActive = false;
                        existingRound.DurationMinutes = (int)Math.Max(0, (groupEnd - groupStart).TotalMinutes);
                        roundsToUpdate.Add(existingRound);
                    }
                    else
                    {
                        // Create new round
                        var serverName = serverNames.GetValueOrDefault(roundServerGuid, "");
                        var newRound = new Round
                        {
                            RoundId = roundId,
                            ServerGuid = roundServerGuid,
                            ServerName = serverName,
                            MapName = mapName,
                            GameType = gameType,
                            StartTime = groupStart,
                            EndTime = groupEnd,
                            IsActive = false,
                            DurationMinutes = (int)Math.Max(0, (groupEnd - groupStart).TotalMinutes)
                        };
                        roundsToAdd.Add(newRound);
                    }
                }

                // Bulk add new rounds
                if (roundsToAdd.Count > 0)
                {
                    _logger.LogInformation("Backfill: Adding {RoundCount} new rounds", roundsToAdd.Count);
                    await _dbContext.Rounds.AddRangeAsync(roundsToAdd, cancellationToken);
                }

                // Bulk update existing rounds
                if (roundsToUpdate.Count > 0)
                {
                    _logger.LogInformation("Backfill: Updating {RoundCount} existing rounds", roundsToUpdate.Count);
                    _dbContext.Rounds.UpdateRange(roundsToUpdate);
                }

                // Save all round changes
                await _dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Backfill: Successfully saved {NewCount} new and {UpdateCount} updated rounds",
                    roundsToAdd.Count, roundsToUpdate.Count);

                // Bulk update PlayerSessions for all rounds in this batch
                var allSessionIds = roundBatch.SelectMany(r => r.SessionIds).ToList();
                _logger.LogInformation("Backfill: Updating {SessionCount} PlayerSessions for {RoundCount} rounds",
                    allSessionIds.Count, roundBatch.Count);

                // Create a mapping of SessionId to RoundId
                var sessionToRoundMap = new Dictionary<int, string>();
                foreach (var (roundId, _, _, _, _, _, sessionIds) in roundBatch)
                {
                    foreach (var sessionId in sessionIds)
                    {
                        sessionToRoundMap[sessionId] = roundId;
                    }
                }

                // Update PlayerSessions in batches using a single SQL statement with CASE
                const int sessionBatchSize = 1000;
                for (int i = 0; i < allSessionIds.Count; i += sessionBatchSize)
                {
                    var sessionBatch = allSessionIds.Skip(i).Take(sessionBatchSize).ToList();
                    var batchNumber = (i / sessionBatchSize) + 1;
                    var totalBatches = (int)Math.Ceiling((double)allSessionIds.Count / sessionBatchSize);

                    _logger.LogInformation("Backfill: Updating session batch {BatchNumber}/{TotalBatches} with {SessionCount} sessions",
                        batchNumber, totalBatches, sessionBatch.Count);

                    // Build a single SQL statement with CASE to update all sessions in the batch
                    var caseStatements = new List<string>();
                    var parameters = new List<object>();
                    var paramIndex = 0;

                    foreach (var sessionId in sessionBatch)
                    {
                        if (sessionToRoundMap.TryGetValue(sessionId, out var roundId))
                        {
                            caseStatements.Add($"WHEN {sessionId} THEN {{{paramIndex}}}");
                            parameters.Add(roundId);
                            paramIndex++;
                        }
                    }

                    if (caseStatements.Count > 0)
                    {
                        var sessionIdList = string.Join(',', sessionBatch);
                        var caseClause = string.Join(' ', caseStatements);
                        var updateSql = $@"
                            UPDATE PlayerSessions 
                            SET RoundId = CASE SessionId 
                                {caseClause}
                                ELSE RoundId 
                            END 
                            WHERE SessionId IN ({sessionIdList})";

                        await _dbContext.Database.ExecuteSqlRawAsync(updateSql, parameters.ToArray(), cancellationToken);

                        _logger.LogInformation("Backfill: Successfully updated {UpdatedCount} sessions in batch {BatchNumber}/{TotalBatches} with single SQL statement",
                            caseStatements.Count, batchNumber, totalBatches);
                    }
                    else
                    {
                        _logger.LogInformation("Backfill: No sessions to update in batch {BatchNumber}/{TotalBatches}",
                            batchNumber, totalBatches);
                    }
                }

                await tx.CommitAsync(cancellationToken);
                _logger.LogInformation("Backfill: Successfully committed round batch {BatchNumber}/{TotalBatches}",
                    (batchIndex / roundBatchSize) + 1, (int)Math.Ceiling((double)roundsToProcess.Count / roundBatchSize));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backfill: Error processing round batch {BatchNumber}/{TotalBatches}. Rolling back transaction. Error: {ErrorMessage}",
                    (batchIndex / roundBatchSize) + 1, (int)Math.Ceiling((double)roundsToProcess.Count / roundBatchSize), ex.Message);
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        }

        // Optionally, mark the very latest round per server as active across the whole database
        if (markLatestPerServerActive)
        {
            _logger.LogInformation("Backfill: Marking latest round per server as active across entire database");
            await MarkLatestRoundsPerServerActiveAsync(serverGuid, cancellationToken);
        }

        // Calculate participant counts for all rounds in a single SQL query
        _logger.LogInformation("Backfill: Calculating participant counts for all {RoundCount} rounds", roundsToProcess.Count);

        var roundIdsForParticipantCount = roundsToProcess.Select(r => r.RoundId).ToList();
        const int participantBatchSize = 100;

        for (int i = 0; i < roundIdsForParticipantCount.Count; i += participantBatchSize)
        {
            var roundIdBatch = roundIdsForParticipantCount.Skip(i).Take(participantBatchSize).ToList();
            var batchNumber = (i / participantBatchSize) + 1;
            var totalBatches = (int)Math.Ceiling((double)roundIdsForParticipantCount.Count / participantBatchSize);

            _logger.LogInformation("Backfill: Calculating participant counts for batch {BatchNumber}/{TotalBatches} with {RoundCount} rounds",
                batchNumber, totalBatches, roundIdBatch.Count);

            // Use a single SQL query to update participant counts for multiple rounds
            var roundIdList = string.Join(',', roundIdBatch.Select(id => $"'{id}'"));
            var updateSql = $@"
                UPDATE Rounds 
                SET ParticipantCount = (
                    SELECT COUNT(DISTINCT ps.PlayerName)
                    FROM PlayerSessions ps
                    JOIN Players p ON ps.PlayerName = p.Name
                    WHERE ps.RoundId = Rounds.RoundId 
                    AND p.AiBot = 0
                )
                WHERE RoundId IN ({roundIdList})";

            await _dbContext.Database.ExecuteSqlRawAsync(updateSql, cancellationToken: cancellationToken);

            _logger.LogInformation("Backfill: Successfully updated participant counts for batch {BatchNumber}/{TotalBatches}",
                batchNumber, totalBatches);
        }

        var endTime = DateTime.UtcNow;
        var duration = endTime - startTime;
        _logger.LogInformation("BackfillRoundsAsync completed: processed {TotalRounds} rounds in {Duration} ({DurationMs}ms). startTimeUtc={StartTimeUtc}, endTimeUtc={EndTimeUtc}, serverGuid={ServerGuid}",
            createdOrUpdatedRounds, duration, duration.TotalMilliseconds, startTimeUtc, endTimeUtc, serverGuid ?? "ALL");

        // Note: To reduce SQL logging noise during bulk operations, consider configuring logging in appsettings.json:
        // "Logging": {
        //   "LogLevel": {
        //     "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
        //   }
        // }

        return createdOrUpdatedRounds;
    }

    // Removed the per-processed-set activation to simplify behavior; use global activation instead

    private static string ComputeRoundId(string serverGuid, string mapName, DateTime startTimeUtc)
    {
        var normalized = new DateTime(startTimeUtc.Ticks - (startTimeUtc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        var payload = $"{serverGuid}|{mapName}|{normalized:yyyy-MM-ddTHH:mm:ssZ}";
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var hex = Convert.ToHexString(hash);
        var roundId = hex[..20].ToLowerInvariant();
        return roundId;
    }

    private async Task MarkLatestRoundsPerServerActiveAsync(string? scopeServerGuid, CancellationToken cancellationToken)
    {
        // Determine which servers to process (all servers or a single server if scoped)
        List<string> serverGuids;
        if (!string.IsNullOrWhiteSpace(scopeServerGuid))
        {
            serverGuids = new List<string> { scopeServerGuid };
        }
        else
        {
            serverGuids = await _dbContext.Rounds
                .Select(r => r.ServerGuid)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        _logger.LogInformation("Backfill: Preparing to activate latest rounds for {ServerCount} servers", serverGuids.Count);

        const int batchSize = 100;
        for (int i = 0; i < serverGuids.Count; i += batchSize)
        {
            var batch = serverGuids.Skip(i).Take(batchSize).ToList();
            var batchNumber = (i / batchSize) + 1;
            var totalBatches = (int)Math.Ceiling((double)serverGuids.Count / batchSize);

            // Compute the last round per server in this batch using Coalesce(EndTime, StartTime)
            var lastRoundIds = await _dbContext.Rounds
                .Where(r => batch.Contains(r.ServerGuid))
                .GroupBy(r => r.ServerGuid)
                .Select(g => g
                    .OrderByDescending(r => (r.EndTime ?? r.StartTime))
                    .Select(r => r.RoundId)
                    .First())
                .ToListAsync(cancellationToken);

            // Activate the winners
            if (lastRoundIds.Count > 0)
            {
                var roundList = string.Join(',', lastRoundIds.Select(id => $"'{id.Replace("'", "''")}'"));
                var activateSql =
                    "UPDATE Rounds\n" +
                    "SET IsActive = 1\n" +
                    "WHERE RoundId IN (" + roundList + ")";
                await _dbContext.Database.ExecuteSqlRawAsync(activateSql, cancellationToken: cancellationToken);
            }

            _logger.LogInformation("Backfill: Marked last round active for batch {BatchNumber}/{TotalBatches} (servers={ServerCount})", batchNumber, totalBatches, batch.Count);
        }

        _logger.LogInformation("Backfill: Completed activation of latest rounds per server");
    }
}


