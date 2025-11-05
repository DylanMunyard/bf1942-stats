using Microsoft.Extensions.Logging;

namespace api.Data.Migrations;

public class BackfillController(RoundBackfillService backfillService, ILogger<BackfillController> logger)
{
    private readonly RoundBackfillService _backfillService = backfillService;
    private readonly ILogger<BackfillController> _logger = logger;

    public class BackfillRequest
    {
        public string? ServerGuid { get; set; }
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        public bool MarkLatestPerServerActive { get; set; } = false;
    }

    public class BackfillResponse
    {
        public int UpsertedRounds { get; set; }
        public string? ServerGuid { get; set; }
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        public DateTime ExecutedAtUtc { get; set; }
        public long DurationMs { get; set; }
    }

    /// <summary>
    /// Used to populate rounds and participant counts for a given time range.
    /// If MarkLatestPerServerActive is true, the latest synced round for each server will be marked as active.
    /// </summary>
    public async Task<BackfillResponse> BackfillRounds(BackfillRequest request, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        _logger.LogInformation("Backfill request started: server={ServerGuid} from={From} to={To}", request.ServerGuid ?? "ALL", request.FromUtc, request.ToUtc);
        var count = await _backfillService.BackfillRoundsAsync(request.FromUtc, request.ToUtc, request.ServerGuid, request.MarkLatestPerServerActive, cancellationToken);
        var ended = DateTime.UtcNow;
        var response = new BackfillResponse
        {
            UpsertedRounds = count,
            ServerGuid = request.ServerGuid,
            FromUtc = request.FromUtc,
            ToUtc = request.ToUtc,
            ExecutedAtUtc = ended,
            DurationMs = (long)(ended - started).TotalMilliseconds
        };
        _logger.LogInformation("Backfill completed: upserted={Count} durationMs={DurationMs}", response.UpsertedRounds, response.DurationMs);
        return response;
    }
}
