namespace New_project.Models;

public sealed class SignupRequest
{
    public string? Email { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public sealed class LoginRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public sealed class CreateMessageRequest
{
    public string? Text { get; set; }
}

public sealed class AdminActionRequest
{
    public string? Action { get; set; }
    public string? TargetUsername { get; set; }
    public int? DurationMinutes { get; set; }
}
