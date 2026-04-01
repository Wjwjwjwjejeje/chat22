namespace New_project.Models;

public sealed class AttachmentRecord
{
    public required string FileName { get; set; }
    public required string StoredFileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string Url { get; set; }
}
