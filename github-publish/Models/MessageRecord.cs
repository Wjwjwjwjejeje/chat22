namespace New_project.Models;

public sealed class MessageRecord
{
    public required string Id { get; set; }
    public required string Username { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public AttachmentRecord? Attachment { get; set; }
    public string? ReplyToMessageId { get; set; }
    public string? EmbedUrl { get; set; }
}
