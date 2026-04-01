namespace New_project.Models;

public sealed class BannedIpRecord
{
    public required string IpAddress { get; set; }
    public required string Username { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
