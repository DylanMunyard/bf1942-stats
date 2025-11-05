namespace api.ClickHouse.Interfaces;

public interface IClickHouseReader
{
    Task<string> ExecuteQueryAsync(string query);
}
