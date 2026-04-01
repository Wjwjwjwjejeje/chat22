namespace New_project.Models;

public sealed class UserRecord
{
    public required string Email { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public int MessageCount { get; set; }
    public int Points { get; set; }
    public DateTimeOffset JoinedAtUtc { get; set; }
}