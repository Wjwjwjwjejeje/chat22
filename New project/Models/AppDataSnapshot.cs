namespace New_project.Models;

public sealed class AppDataSnapshot
{
    public List<UserRecord> Users { get; set; } = [];
    public List<SessionRecord> Sessions { get; set; } = [];
    public List<MessageRecord> Messages { get; set; } = [];
    public List<PresenceRecord> Presence { get; set; } = [];
}