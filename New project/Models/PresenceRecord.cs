namespace New_project.Models;

public sealed class PresenceRecord
{
    public required string Username { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}