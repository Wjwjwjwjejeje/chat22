using System.Text.RegularExpressions;
using New_project.Models;
using New_project.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ChatStore>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/auth/signup", (SignupRequest request, ChatStore store) =>
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

    var result = store.Signup(email, username, password);
    return result.Success
        ? Results.Ok(new { message = result.Message, token = result.Token, username = result.User!.Username })
        : Results.BadRequest(new { message = result.Message });
});

app.MapPost("/api/auth/login", (LoginRequest request, ChatStore store) =>
{
    var username = (request.Username ?? string.Empty).Trim();
    var password = request.Password ?? string.Empty;

    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
    {
        return Results.BadRequest(new { message = "Enter both username and password." });
    }

    var result = store.Login(username, password);
    return result.Success
        ? Results.Ok(new { message = result.Message, token = result.Token, username = result.User!.Username })
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
        : Results.Ok(new { username = user.Username, email = user.Email });
});

app.MapGet("/api/chat/state", (HttpRequest httpRequest, ChatStore store) =>
{
    var user = Authorize(httpRequest, store, updatePresence: true);
    return user is null
        ? Results.Unauthorized()
        : Results.Ok(store.GetChatState(user.Username));
});

app.MapPost("/api/chat/messages", (HttpRequest httpRequest, CreateMessageRequest request, ChatStore store) =>
{
    var user = Authorize(httpRequest, store, updatePresence: true);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var text = (request.Text ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(text))
    {
        return Results.BadRequest(new { message = "Message cannot be empty." });
    }

    if (text.Length > 280)
    {
        return Results.BadRequest(new { message = "Message is too long." });
    }

    var result = store.AddMessage(user.Username, text);
    return result.Success
        ? Results.Ok(new { message = result.Message })
        : Results.BadRequest(new { message = result.Message });
});

app.Run();

static UserRecord? Authorize(HttpRequest request, ChatStore store, bool updatePresence = false)
{
    var token = ReadBearerToken(request);
    return string.IsNullOrWhiteSpace(token) ? null : store.GetUserByToken(token, updatePresence);
}

static string? ReadBearerToken(HttpRequest request)
{
    var header = request.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        ? header[prefix.Length..].Trim()
        : null;
}