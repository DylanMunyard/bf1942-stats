using System.Net.Http;
using System.Text;
using junie_des_1942stats.ClickHouse.Interfaces;

namespace junie_des_1942stats.ClickHouse.Base;

public abstract class BaseClickHouseService
{
    protected readonly HttpClient _httpClient;
    protected readonly string _clickHouseUrl;

    protected BaseClickHouseService(HttpClient httpClient, string clickHouseUrl)
    {
        _httpClient = httpClient;
        _clickHouseUrl = clickHouseUrl.TrimEnd('/');
    }

    protected async Task<string> ExecuteQueryInternalAsync(string query)
    {
        var content = new StringContent(query, Encoding.UTF8, "text/plain");
        var response = await _httpClient.PostAsync($"{_clickHouseUrl}/", content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"ClickHouse query failed: {response.StatusCode} - {errorContent}");
        }

        return await response.Content.ReadAsStringAsync();
    }

    protected async Task ExecuteCommandInternalAsync(string command)
    {
        var content = new StringContent(command, Encoding.UTF8, "text/plain");
        var response = await _httpClient.PostAsync($"{_clickHouseUrl}/", content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
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