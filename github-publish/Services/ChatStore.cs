using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using New_project.Models;

namespace New_project.Services;

public sealed class ChatStore
{
    private static readonly TimeSpan PresenceTtl = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PermanentMuteDuration = TimeSpan.FromDays(3650);
    private readonly object _lock = new();
    private readonly string _dataFilePath;
    private readonly string _uploadsDirectory;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private AppDataSnapshot _data;

    public ChatStore(IWebHostEnvironment environment)
    {
        var dataDirectory = Path.Combine(environment.ContentRootPath, "Data");
        _uploadsDirectory = Path.Combine(environment.WebRootPath, "uploads");
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(_uploadsDirectory);
        _dataFilePath = Path.Combine(dataDirectory, "chat-data.json");
        _data = LoadFromDisk();
    }

    public (bool Success, string Message, string? Token, UserRecord? User) Signup(string email, string username, string password, string clientIp)
    {
        lock (_lock)
        {
            CleanupPresence();

            if (IsIpBanned(clientIp))
            {
                return (false, "This IP address has been banned.", null, null);
            }

            if (_data.Users.Any(user => string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)))
            {
                return (false, "That Gmail address is already registered.", null, null);
            }

            if (_data.Users.Any(user => string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase)))
            {
                return (false, "That username is already taken.", null, null);
            }

            var user = new UserRecord
            {
                Email = email,
                Username = username,
                PasswordHash = HashPassword(password),
                MessageCount = 0,
                Points = 0,
                JoinedAtUtc = DateTimeOffset.UtcNow,
                LastKnownIp = clientIp
            };

            _data.Users.Add(user);
            var session = CreateSession(username);
            TouchPresence(username);
            SaveToDisk();
            return (true, "Signup successful.", session.Token, CloneUser(user));
        }
    }

    public (bool Success, string Message, string? Token, UserRecord? User) Login(string username, string password, string clientIp)
    {
        lock (_lock)
        {
            CleanupPresence();

            if (IsIpBanned(clientIp))
            {
                return (false, "This IP address has been banned.", null, null);
            }

            var user = _data.Users.FirstOrDefault(entry =>
                string.Equals(entry.Username, username, StringComparison.OrdinalIgnoreCase));

            if (user is null)
            {
                return (false, "Username not found.", null, null);
            }

            if (user.IsBanned)
            {
                return (false, "This account has been banned.", null, null);
            }

            if (!string.Equals(user.PasswordHash, HashPassword(password), StringComparison.Ordinal))
            {
                return (false, "Password is incorrect.", null, null);
            }

            user.LastKnownIp = clientIp;
            var session = CreateSession(user.Username);
            TouchPresence(user.Username);
            SaveToDisk();
            return (true, "Login successful.", session.Token, CloneUser(user));
        }
    }

    public void Logout(string token)
    {
        lock (_lock)
        {
            var session = _data.Sessions.FirstOrDefault(entry => entry.Token == token);
            if (session is null)
            {
                return;
            }

            _data.Sessions.Remove(session);
            CleanupPresence();
            SaveToDisk();
        }
    }

    public UserRecord? GetUserByToken(string token, bool updatePresence = false, string? clientIp = null)
    {
        lock (_lock)
        {
            CleanupPresence();
            var session = _data.Sessions.FirstOrDefault(entry => entry.Token == token);
            if (session is null)
            {
                SaveToDisk();
                return null;
            }

            var user = _data.Users.FirstOrDefault(entry => entry.Username == session.Username);
            if (user is null)
            {
                _data.Sessions.Remove(session);
                SaveToDisk();
                return null;
            }

            if ((clientIp is not null && IsIpBanned(clientIp)) || user.IsBanned)
            {
                _data.Sessions.RemoveAll(entry => entry.Username == user.Username);
                SaveToDisk();
                return null;
            }

            if (!string.IsNullOrWhiteSpace(clientIp))
            {
                user.LastKnownIp = clientIp;
            }

            if (updatePresence)
            {
                TouchPresence(user.Username);
            }

            SaveToDisk();
            return CloneUser(user);
        }
    }

    public object GetChatState(string username)
    {
        lock (_lock)
        {
            CleanupPresence();
            TouchPresence(username);
            SaveToDisk();

            var usersByName = _data.Users.ToDictionary(entry => entry.Username, StringComparer.OrdinalIgnoreCase);
            var messagesById = _data.Messages.ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
            var currentUser = usersByName[username];

            return new
            {
                currentUser = new
                {
                    username,
                    isAdmin = IsAdminUsername(username),
                    isMuted = IsUserMuted(currentUser),
                    mutedUntilUtc = currentUser.MutedUntilUtc
                },
                onlineUsers = _data.Presence
                    .OrderBy(entry => entry.Username)
                    .Select(entry => entry.Username)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                leaderboard = _data.Users
                    .OrderByDescending(entry => entry.Points)
                    .ThenByDescending(entry => entry.MessageCount)
                    .ThenBy(entry => entry.Username)
                    .Select(entry => new
                    {
                        username = entry.Username,
                        points = entry.Points,
                        messageCount = entry.MessageCount,
                        isMuted = IsUserMuted(entry),
                        isBanned = entry.IsBanned
                    })
                    .ToArray(),
                messages = _data.Messages
                    .OrderBy(entry => entry.CreatedAtUtc)
                    .Select(entry => new
                    {
                        id = entry.Id,
                        username = entry.Username,
                        text = entry.Text,
                        createdAtUtc = entry.CreatedAtUtc,
                        embedUrl = entry.EmbedUrl,
                        attachment = entry.Attachment is null
                            ? null
                            : new
                            {
                                fileName = entry.Attachment.FileName,
                                contentType = entry.Attachment.ContentType,
                                sizeBytes = entry.Attachment.SizeBytes,
                                url = entry.Attachment.Url
                            },
                        reply = entry.ReplyToMessageId is not null && messagesById.TryGetValue(entry.ReplyToMessageId, out var reply)
                            ? new
                            {
                                id = reply.Id,
                                username = reply.Username,
                                text = reply.Text,
                                attachmentFileName = reply.Attachment?.FileName
                            }
                            : null,
                        canDelete = string.Equals(entry.Username, username, StringComparison.OrdinalIgnoreCase) || IsAdminUsername(username)
                    })
                    .ToArray()
            };
        }
    }

    public (bool Success, string Message) AddMessage(string username, string text, AttachmentRecord? attachment = null, string? replyToMessageId = null, string? embedUrl = null)
    {
        lock (_lock)
        {
            CleanupPresence();
            TouchPresence(username);

            var user = _data.Users.FirstOrDefault(entry => entry.Username == username);
            if (user is null)
            {
                return (false, "User account was not found.");
            }

            if (user.IsBanned)
            {
                return (false, "This account has been banned.");
            }

            if (IsUserMuted(user))
            {
                return (false, "You are muted and cannot post right now.");
            }

            if (!string.IsNullOrWhiteSpace(replyToMessageId) && _data.Messages.All(entry => entry.Id != replyToMessageId))
            {
                return (false, "The message you replied to no longer exists.");
            }

            _data.Messages.Add(new MessageRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Username = username,
                Text = text,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Attachment = attachment,
                ReplyToMessageId = string.IsNullOrWhiteSpace(replyToMessageId) ? null : replyToMessageId,
                EmbedUrl = string.IsNullOrWhiteSpace(embedUrl) ? null : embedUrl
            });

            user.MessageCount += 1;
            user.Points = user.MessageCount * 10;
            SaveToDisk();
            return (true, "Message sent.");
        }
    }

    public async Task<AttachmentRecord> SaveAttachmentAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var originalFileName = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(originalFileName);
        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var destinationPath = Path.Combine(_uploadsDirectory, storedFileName);

        await using var stream = File.Create(destinationPath);
        await file.CopyToAsync(stream, cancellationToken);

        return new AttachmentRecord
        {
            FileName = originalFileName,
            StoredFileName = storedFileName,
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            SizeBytes = file.Length,
            Url = $"/uploads/{storedFileName}"
        };
    }

    public (bool Success, string Message) DeleteMessage(string actorUsername, string messageId)
    {
        lock (_lock)
        {
            var message = _data.Messages.FirstOrDefault(entry => entry.Id == messageId);
            if (message is null)
            {
                return (false, "Message not found.");
            }

            var isAdmin = IsAdminUsername(actorUsername);
            if (!isAdmin && !string.Equals(message.Username, actorUsername, StringComparison.OrdinalIgnoreCase))
            {
                return (false, "You cannot delete that message.");
            }

            DeleteAttachmentFile(message.Attachment);
            _data.Messages.Remove(message);

            foreach (var child in _data.Messages.Where(entry => entry.ReplyToMessageId == messageId))
            {
                child.ReplyToMessageId = null;
            }

            SaveToDisk();
            return (true, "Message deleted.");
        }
    }

    public (bool Success, string Message) ApplyAdminAction(string actorUsername, string action, string? targetUsername, int? durationMinutes)
    {
        lock (_lock)
        {
            if (!IsAdminUsername(actorUsername))
            {
                return (false, "Admin access required.");
            }

            var normalizedAction = (action ?? string.Empty).Trim().ToLowerInvariant();

            if (normalizedAction == "clear")
            {
                foreach (var message in _data.Messages)
                {
                    DeleteAttachmentFile(message.Attachment);
                }

                _data.Messages.Clear();
                SaveToDisk();
                return (true, "Chat cleared.");
            }

            var target = _data.Users.FirstOrDefault(entry => string.Equals(entry.Username, targetUsername, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                return (false, "Target user not found.");
            }

            switch (normalizedAction)
            {
                case "kick":
                    _data.Sessions.RemoveAll(entry => string.Equals(entry.Username, target.Username, StringComparison.OrdinalIgnoreCase));
                    _data.Presence.RemoveAll(entry => string.Equals(entry.Username, target.Username, StringComparison.OrdinalIgnoreCase));
                    SaveToDisk();
                    return (true, $"{target.Username} was kicked.");
                case "mute":
                    target.MutedUntilUtc = DateTimeOffset.UtcNow.Add(PermanentMuteDuration);
                    SaveToDisk();
                    return (true, $"{target.Username} was muted.");
                case "unmute":
                    target.MutedUntilUtc = null;
                    SaveToDisk();
                    return (true, $"{target.Username} was unmuted.");
                case "timeout":
                    var minutes = Math.Clamp(durationMinutes ?? 10, 1, 10080);
                    target.MutedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(minutes);
                    SaveToDisk();
                    return (true, $"{target.Username} was timed out for {minutes} minutes.");
                case "ban":
                    target.IsBanned = true;
                    target.MutedUntilUtc = null;
                    _data.Sessions.RemoveAll(entry => string.Equals(entry.Username, target.Username, StringComparison.OrdinalIgnoreCase));
                    _data.Presence.RemoveAll(entry => string.Equals(entry.Username, target.Username, StringComparison.OrdinalIgnoreCase));
                    SaveToDisk();
                    return (true, $"{target.Username} was banned.");
                case "unban":
                    target.IsBanned = false;
                    SaveToDisk();
                    return (true, $"{target.Username} was unbanned.");
                case "ipban":
                    if (string.IsNullOrWhiteSpace(target.LastKnownIp))
                    {
                        return (false, "No IP is available for that user yet.");
                    }

                    if (!_data.BannedIps.Any(entry => entry.IpAddress == target.LastKnownIp))
                    {
                        _data.BannedIps.Add(new BannedIpRecord
                        {
                            IpAddress = target.LastKnownIp,
                            Username = target.Username,
                            CreatedAtUtc = DateTimeOffset.UtcNow
                        });
                    }

                    target.IsBanned = true;
                    _data.Sessions.RemoveAll(entry => string.Equals(entry.Username, target.Username, StringComparison.OrdinalIgnoreCase));
                    _data.Presence.RemoveAll(entry => string.Equals(entry.Username, target.Username, StringComparison.OrdinalIgnoreCase));
                    SaveToDisk();
                    return (true, $"{target.Username} was IP banned.");
                default:
                    return (false, "Unknown admin action.");
            }
        }
    }

    public static bool IsAdminUsername(string username) =>
        string.Equals(username, "sonickid22", StringComparison.OrdinalIgnoreCase);

    private bool IsIpBanned(string clientIp) =>
        _data.BannedIps.Any(entry => string.Equals(entry.IpAddress, clientIp, StringComparison.OrdinalIgnoreCase));

    private static bool IsUserMuted(UserRecord user) =>
        user.MutedUntilUtc.HasValue && user.MutedUntilUtc.Value > DateTimeOffset.UtcNow;

    private SessionRecord CreateSession(string username)
    {
        var session = new SessionRecord
        {
            Token = Guid.NewGuid().ToString("N"),
            Username = username,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        _data.Sessions.RemoveAll(entry => entry.Username == username);
        _data.Sessions.Add(session);
        return session;
    }

    private void TouchPresence(string username)
    {
        _data.Presence.RemoveAll(entry => string.Equals(entry.Username, username, StringComparison.OrdinalIgnoreCase));
        _data.Presence.Add(new PresenceRecord
        {
            Username = username,
            LastSeenUtc = DateTimeOffset.UtcNow
        });
    }

    private void CleanupPresence()
    {
        var cutoff = DateTimeOffset.UtcNow - PresenceTtl;
        _data.Presence.RemoveAll(entry => entry.LastSeenUtc < cutoff);
    }

    private void DeleteAttachmentFile(AttachmentRecord? attachment)
    {
        if (attachment is null)
        {
            return;
        }

        var path = Path.Combine(_uploadsDirectory, attachment.StoredFileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private AppDataSnapshot LoadFromDisk()
    {
        if (!File.Exists(_dataFilePath))
        {
            return new AppDataSnapshot();
        }

        var json = File.ReadAllText(_dataFilePath);
        return JsonSerializer.Deserialize<AppDataSnapshot>(json, _jsonOptions) ?? new AppDataSnapshot();
    }

    private void SaveToDisk()
    {
        var json = JsonSerializer.Serialize(_data, _jsonOptions);
        File.WriteAllText(_dataFilePath, json);
    }

    private static string HashPassword(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes);
    }

    private static UserRecord CloneUser(UserRecord user) =>
        new()
        {
            Email = user.Email,
            Username = user.Username,
            PasswordHash = user.PasswordHash,
            MessageCount = user.MessageCount,
            Points = user.Points,
            JoinedAtUtc = user.JoinedAtUtc,
            MutedUntilUtc = user.MutedUntilUtc,
            IsBanned = user.IsBanned,
            LastKnownIp = user.LastKnownIp
        };
}
