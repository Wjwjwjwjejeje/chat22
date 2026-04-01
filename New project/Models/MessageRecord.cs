namespace New_project.Models;

public sealed class MessageRecord
{
    public required string Id { get; set; }
    public required string Username { get; set; }
    public required string Text { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}