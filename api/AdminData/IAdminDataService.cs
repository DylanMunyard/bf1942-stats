using api.AdminData.Models;
using api.Data.Entities;

namespace api.AdminData;

public interface IAdminDataService
{
    Task<PagedResult<SuspiciousSessionResponse>> QuerySuspiciousSessionsAsync(QuerySuspiciousSessionsRequest request);
    Task<RoundDetailResponse?> GetRoundDetailAsync(string roundId);
    Task<DeleteRoundResponse> DeleteRoundAsync(string roundId, string adminEmail);
    Task<BulkDeleteRoundsResponse> BulkDeleteRoundsAsync(IReadOnlyList<string> roundIds, string adminEmail);
    Task<UndeleteRoundResponse> UndeleteRoundAsync(string roundId, string adminEmail);
    Task<List<AdminAuditLog>> GetAuditLogAsync(int limit = 100);
}
