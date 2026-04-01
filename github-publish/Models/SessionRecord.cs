namespace New_project.Models;

public sealed class SessionRecord
{
    public required string Token { get; set; }
    public required string Username { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}