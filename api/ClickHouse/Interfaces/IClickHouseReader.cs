using System.Net.Http;

namespace junie_des_1942stats.ClickHouse.Interfaces;

public interface IClickHouseReader
{
    Task<string> ExecuteQueryAsync(string query);
}