using System.Text.RegularExpressions;
using New_project.Models;
using New_project.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ChatStore>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/auth/signup", (HttpRequest httpRequest, SignupRequest request, ChatStore store) =>
{
    var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
    var username = (request.Username ?? string.Empty).Trim();
    var password = request.Password ?? string.Empty;

    if (!Regex.IsMatch(email, @"^[^\s@]+@gmail\.com$", RegexOptions.IgnoreCase))
    {
        return Results.BadRequest(new { message = "Only Gmail addresses ending in @gmail.com are allowed." });
    }

    if (username.Length is < 3 or > 20)
    {
        return Results.BadRequest(new { message = "Username must be between 3 and 20 characters." });
    }

    if (password.Length < 4)
    {
        return Results.BadRequest(new { message = "Password must be at least 4 characters." });
    }

    var result = store.Signup(email, username, password, GetClientIp(httpRequest));
    return result.Success
        ? Results.Ok(new { message = result.Message, token = result.Token, username = result.User!.Username, isAdmin = ChatStore.IsAdminUsername(result.User.Username) })
        : Results.BadRequest(new { message = result.Message });
});

app.MapPost("/api/auth/login", (HttpRequest httpRequest, LoginRequest request, ChatStore store) =>
{
    var username = (request.Username ?? string.Empty).Trim();
    var password = request.Password ?? string.Empty;

    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
    {
        return Results.BadRequest(new { message = "Enter both username and password." });
    }

    var result = store.Login(username, password, GetClientIp(httpRequest));
    return result.Success
        ? Results.Ok(new { message = result.Message, token = result.Token, username = result.User!.Username, isAdmin = ChatStore.IsAdminUsername(result.User.Username) })
        : Results.BadRequest(new { message = result.Message });
});

app.MapPost("/api/auth/logout", (HttpRequest httpRequest, ChatStore store) =>
{
    var token = ReadBearerToken(httpRequest);
    if (string.IsNullOrWhiteSpace(token))
    {
        return Results.Unauthorized();
    }

    store.Logout(token);
    return Results.Ok(new { message = "Logged out." });
});

app.MapGet("/api/auth/me", (HttpRequest httpRequest, ChatStore store) =>
{
    var user = Authorize(httpRequest, store);
    return user is null
        ? Results.Unauthorized()
        : Results.Ok(new { username = user.Username, email = user.Email, isAdmin = ChatStore.IsAdminUsername(user.Username) });
});

app.MapGet("/api/chat/state", (HttpRequest httpRequest, ChatStore store) =>
{
    var user = Authorize(httpRequest, store, updatePresence: true);
    return user is null
        ? Results.Unauthorized()
        : Results.Ok(store.GetChatState(user.Username));
});

app.MapPost("/api/chat/messages", async (HttpRequest httpRequest, ChatStore store, CancellationToken cancellationToken) =>
{
    var user = Authorize(httpRequest, store, updatePresence: true);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var form = await httpRequest.ReadFormAsync(cancellationToken);
    var text = (form["text"].ToString() ?? string.Empty).Trim();
    var embedUrl = (form["embedUrl"].ToString() ?? string.Empty).Trim();
    var replyToMessageId = (form["replyToMessageId"].ToString() ?? string.Empty).Trim();
    var file = form.Files["file"];

    if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(embedUrl) && file is null)
    {
        return Results.BadRequest(new { message = "Add a message, an embed link, a file, or a photo." });
    }

    if (text.Length > 280)
    {
        return Results.BadRequest(new { message = "Message is too long." });
    }

    Uri? parsedUri = null;
    if (!string.IsNullOrWhiteSpace(embedUrl) && !Uri.TryCreate(embedUrl, UriKind.Absolute, out parsedUri))
    {
        return Results.BadRequest(new { message = "Embed links must be valid URLs." });
    }

    if (!string.IsNullOrWhiteSpace(embedUrl) && parsedUri is not null && parsedUri.Scheme is not ("http" or "https"))
    {
        return Results.BadRequest(new { message = "Embed links must start with http or https." });
    }

    AttachmentRecord? attachment = null;
    if (file is not null)
    {
        const long maxFileBytes = 10 * 1024 * 1024;
        if (file.Length == 0)
        {
            return Results.BadRequest(new { message = "The selected file is empty." });
        }

        if (file.Length > maxFileBytes)
        {
            return Results.BadRequest(new { message = "Files must be 10 MB or smaller." });
        }

        attachment = await store.SaveAttachmentAsync(file, cancellationToken);
    }

    var result = store.AddMessage(
        user.Username,
        text,
        attachment,
        string.IsNullOrWhiteSpace(replyToMessageId) ? null : replyToMessageId,
        string.IsNullOrWhiteSpace(embedUrl) ? null : embedUrl);

    return result.Success
        ? Results.Ok(new { message = result.Message })
        : Results.BadRequest(new { message = result.Message });
});

app.MapDelete("/api/chat/messages/{messageId}", (HttpRequest httpRequest, string messageId, ChatStore store) =>
{
    var user = Authorize(httpRequest, store, updatePresence: true);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var result = store.DeleteMessage(user.Username, messageId);
    return result.Success
        ? Results.Ok(new { message = result.Message })
        : Results.BadRequest(new { message = result.Message });
});

app.MapPost("/api/admin/actions", (HttpRequest httpRequest, AdminActionRequest request, ChatStore store) =>
{
    var user = Authorize(httpRequest, store, updatePresence: true);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var result = store.ApplyAdminAction(user.Username, request.Action ?? string.Empty, request.TargetUsername, request.DurationMinutes);
    return result.Success
        ? Results.Ok(new { message = result.Message })
        : Results.BadRequest(new { message = result.Message });
});

app.Run();

static UserRecord? Authorize(HttpRequest request, ChatStore store, bool updatePresence = false)
{
    var token = ReadBearerToken(request);
    return string.IsNullOrWhiteSpace(token) ? null : store.GetUserByToken(token, updatePresence, GetClientIp(request));
}

static string? ReadBearerToken(HttpRequest request)
{
    var header = request.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        ? header[prefix.Length..].Trim()
        : null;
}

static string GetClientIp(HttpRequest request)
{
    var forwarded = request.Headers["X-Forwarded-For"].ToString();
    if (!string.IsNullOrWhiteSpace(forwarded))
    {
        return forwarded.Split(',')[0].Trim();
    }

    return request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
