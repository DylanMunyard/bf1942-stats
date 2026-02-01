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

    /// <summary>Get a key-value entry from app_data table. Returns null if key does not exist.</summary>
    Task<AppDataRow?> GetAppDataAsync(string key);
    /// <summary>Upsert a key-value entry. Invalidates initial-data cache when key is site_notice.</summary>
    Task SetAppDataAsync(string key, string value);
    /// <summary>Delete a key-value entry. Invalidates initial-data cache when key is site_notice.</summary>
    Task DeleteAppDataAsync(string key);
}

public record AppDataRow(string Id, string Value, DateTime UpdatedAt);
