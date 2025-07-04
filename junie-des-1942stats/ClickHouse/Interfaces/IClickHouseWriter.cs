namespace junie_des_1942stats.ClickHouse.Interfaces;

public interface IClickHouseWriter
{
    Task ExecuteCommandAsync(string command);
}