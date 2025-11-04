namespace api.ClickHouse.Interfaces;

public interface IClickHouseWriter
{
    Task ExecuteCommandAsync(string command);
}