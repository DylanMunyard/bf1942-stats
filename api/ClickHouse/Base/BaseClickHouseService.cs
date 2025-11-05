using System.Text;
using api.ClickHouse.Interfaces;
using api.Telemetry;
using System.Diagnostics;

namespace api.ClickHouse.Base;

public abstract class BaseClickHouseService(HttpClient httpClient, string clickHouseUrl)
{
    protected readonly HttpClient _httpClient = httpClient;
    protected readonly string _clickHouseUrl = clickHouseUrl.TrimEnd('/');

    protected async Task<string> ExecuteQueryInternalAsync(string query)
    {
        using var activity = ActivitySources.ClickHouse.StartActivity("ClickHouse.Query");
        activity?.SetTag("clickhouse.url", _clickHouseUrl);
        activity?.SetTag("clickhouse.query", query.Length > 500 ? query[..500] + "..." : query);
        activity?.SetTag("clickhouse.operation", "query");

        var content = new StringContent(query, Encoding.UTF8, "text/plain");
        var response = await _httpClient.PostAsync($"{_clickHouseUrl}/", content);

        activity?.SetTag("clickhouse.status_code", (int)response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            activity?.SetTag("clickhouse.error", errorContent);
            activity?.SetStatus(ActivityStatusCode.Error, $"ClickHouse query failed: {response.StatusCode}");
            throw new Exception($"ClickHouse query failed: {response.StatusCode} - {errorContent}");
        }

        var result = await response.Content.ReadAsStringAsync();
        activity?.SetTag("clickhouse.result_size", result.Length);
        return result;
    }

    protected async Task ExecuteCommandInternalAsync(string command)
    {
        using var activity = ActivitySources.ClickHouse.StartActivity("ClickHouse.Command");
        activity?.SetTag("clickhouse.url", _clickHouseUrl);
        activity?.SetTag("clickhouse.command", command.Length > 500 ? command[..500] + "..." : command);
        activity?.SetTag("clickhouse.operation", "command");

        var content = new StringContent(command, Encoding.UTF8, "text/plain");
        var response = await _httpClient.PostAsync($"{_clickHouseUrl}/", content);

        activity?.SetTag("clickhouse.status_code", (int)response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            activity?.SetTag("clickhouse.error", errorContent);
            activity?.SetStatus(ActivityStatusCode.Error, $"ClickHouse command failed: {response.StatusCode}");
            throw new Exception($"ClickHouse command failed: {response.StatusCode} - {errorContent}");
        }
    }
}

public class ClickHouseReader : BaseClickHouseService, IClickHouseReader
{
    public ClickHouseReader(HttpClient httpClient, string clickHouseUrl)
        : base(httpClient, clickHouseUrl)
    {
    }

    public async Task<string> ExecuteQueryAsync(string query)
    {
        return await ExecuteQueryInternalAsync(query);
    }
}

public class ClickHouseWriter : BaseClickHouseService, IClickHouseWriter
{
    public ClickHouseWriter(HttpClient httpClient, string clickHouseUrl)
        : base(httpClient, clickHouseUrl)
    {
    }

    public async Task ExecuteCommandAsync(string command)
    {
        await ExecuteCommandInternalAsync(command);
    }
}
