namespace junie_des_1942stats.Bflist;

public class ServerFilteringConfig
{
    /// <summary>
    /// List of server names that should be filtered out (stuck servers that haven't been online for years)
    /// </summary>
    public List<string> StuckServers { get; set; } = new()
    {
        "Tragic! [USA] - Dallas"
    };
}
