namespace api.Bflist.Models;

public class BfvietnamServersResponse : ServerListResponse
{
    public new BfvietnamServerInfo[] Servers { get; set; } = [];
    public string? Cursor { get; set; }
    public bool HasMore { get; set; }
}
