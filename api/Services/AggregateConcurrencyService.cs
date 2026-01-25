namespace api.Services;

/// <summary>
/// In-memory locks (SemaphoreSlim) for aggregate recalculation. Single process only.
/// </summary>
public class AggregateConcurrencyService : IAggregateConcurrencyService
{
    private readonly SemaphoreSlim _playerAggregates = new(1, 1);
    private readonly SemaphoreSlim _serverMapStats = new(1, 1);
    private readonly SemaphoreSlim _serverPlayerRankings = new(1, 1);

    public async Task ExecuteWithPlayerAggregatesLockAsync(Func<CancellationToken, Task> work, CancellationToken ct = default)
    {
        await _playerAggregates.WaitAsync(ct);
        try
        {
            await work(ct);
        }
        finally
        {
            _playerAggregates.Release();
        }
    }

    public async Task<T> ExecuteWithPlayerAggregatesLockAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        await _playerAggregates.WaitAsync(ct);
        try
        {
            return await work(ct);
        }
        finally
        {
            _playerAggregates.Release();
        }
    }

    public async Task ExecuteWithServerMapStatsLockAsync(Func<CancellationToken, Task> work, CancellationToken ct = default)
    {
        await _serverMapStats.WaitAsync(ct);
        try
        {
            await work(ct);
        }
        finally
        {
            _serverMapStats.Release();
        }
    }

    public async Task<T> ExecuteWithServerMapStatsLockAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        await _serverMapStats.WaitAsync(ct);
        try
        {
            return await work(ct);
        }
        finally
        {
            _serverMapStats.Release();
        }
    }

    public async Task ExecuteWithServerPlayerRankingsLockAsync(Func<CancellationToken, Task> work, CancellationToken ct = default)
    {
        await _serverPlayerRankings.WaitAsync(ct);
        try
        {
            await work(ct);
        }
        finally
        {
            _serverPlayerRankings.Release();
        }
    }

    public async Task<T> ExecuteWithServerPlayerRankingsLockAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        await _serverPlayerRankings.WaitAsync(ct);
        try
        {
            return await work(ct);
        }
        finally
        {
            _serverPlayerRankings.Release();
        }
    }
}
