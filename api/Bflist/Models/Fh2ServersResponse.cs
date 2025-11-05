namespace api.Bflist.Models;

public class Fh2ServersResponse : ServerListResponse
{
    public new Fh2ServerInfo[] Servers { get; set; } = [];
    public string? Cursor { get; set; }
    public bool HasMore { get; set; }
}
