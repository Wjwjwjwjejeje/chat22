using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using New_project.Models;

namespace New_project.Services;

public sealed class ChatStore
{
    private static readonly TimeSpan PresenceTtl = TimeSpan.FromSeconds(45);
    private readonly object _lock = new();
    private readonly string _dataFilePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private AppDataSnapshot _data;

    public ChatStore(IWebHostEnvironment environment)
    {
        var dataDirectory = Path.Combine(environment.ContentRootPath, "Data");
        Directory.CreateDirectory(dataDirectory);
        _dataFilePath = Path.Combine(dataDirectory, "chat-data.json");
        _data = LoadFromDisk();
    }

    public (bool Success, string Message, string? Token, UserRecord? User) Signup(string email, string username, string password)
    {
        lock (_lock)
        {
            CleanupPresence();

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
                JoinedAtUtc = DateTimeOffset.UtcNow
            };

            _data.Users.Add(user);
            var session = CreateSession(username);
            TouchPresence(username);
            SaveToDisk();
            return (true, "Signup successful.", session.Token, user);
        }
    }

    public (bool Success, string Message, string? Token, UserRecord? User) Login(string username, string password)
    {
        lock (_lock)
        {
            CleanupPresence();

            var user = _data.Users.FirstOrDefault(entry =>
                string.Equals(entry.Username, username, StringComparison.OrdinalIgnoreCase));

            if (user is null)
            {
                return (false, "Username not found.", null, null);
            }

            if (!string.Equals(user.PasswordHash, HashPassword(password), StringComparison.Ordinal))
            {
                return (false, "Password is incorrect.", null, null);
            }

            var session = CreateSession(user.Username);
            TouchPresence(user.Username);
            SaveToDisk();
            return (true, "Login successful.", session.Token, user);
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

    public UserRecord? GetUserByToken(string token, bool updatePresence = false)
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

            if (updatePresence)
            {
                TouchPresence(user.Username);
                SaveToDisk();
            }

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

            return new
            {
                currentUser = username,
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
                        messageCount = entry.MessageCount
                    })
                    .ToArray(),
                messages = _data.Messages
                    .OrderBy(entry => entry.CreatedAtUtc)
                    .Select(entry => new
                    {
                        id = entry.Id,
                        username = entry.Username,
                        text = entry.Text,
                        createdAtUtc = entry.CreatedAtUtc
                    })
                    .ToArray()
            };
        }
    }

    public (bool Success, string Message) AddMessage(string username, string text)
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

            _data.Messages.Add(new MessageRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Username = username,
                Text = text,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });

            user.MessageCount += 1;
            user.Points = user.MessageCount * 10;
            SaveToDisk();
            return (true, "Message sent.");
        }
    }

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
            JoinedAtUtc = user.JoinedAtUtc
        };
}