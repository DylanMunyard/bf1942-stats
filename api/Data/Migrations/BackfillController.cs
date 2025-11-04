using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using api.PlayerTracking;

namespace api.Data.Migrations;

[ApiController]
[Route("stats/admin/[controller]")]
public class BackfillController : ControllerBase
{
    private readonly RoundBackfillService _backfillService;
    private readonly ILogger<BackfillController> _logger;

    public BackfillController(RoundBackfillService backfillService, ILogger<BackfillController> logger)
    {
        _backfillService = backfillService;
        _logger = logger;
    }

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

    /* Used to populate rounds and participant counts for a given time range.
     * If MarkLatestPerServerActive is true, the latest synced round for each server will be marked as active.
    */
    [HttpPost("rounds")]
    public async Task<ActionResult<BackfillResponse>> BackfillRounds([FromBody] BackfillRequest request, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "BackfillController is deprecated and disabled. Returning 403 Forbidden. Request: server={ServerGuid} from={From} to={To}",
            request.ServerGuid ?? "ALL", request.FromUtc, request.ToUtc);

        return StatusCode(403, new
        {
            message = "BackfillController is deprecated and no longer available. This endpoint has been disabled for security reasons.",
            timestamp = DateTime.UtcNow
        });

        /*
         var started = DateTime.UtcNow;
         _logger.LogInformation("API backfill request started: server={ServerGuid} from={From} to={To}", request.ServerGuid ?? "ALL", request.FromUtc, request.ToUtc);
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
         _logger.LogInformation("API backfill completed: upserted={Count} durationMs={DurationMs}", response.UpsertedRounds, response.DurationMs);
         return Ok(response);
         */

    }
}
