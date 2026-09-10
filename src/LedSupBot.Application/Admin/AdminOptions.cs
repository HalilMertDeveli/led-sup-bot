namespace LedSupBot.Application.Admin;

/// <summary>Local admin dashboard authentication. Secrets stay server-side only.</summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>Env: ADMIN_DASHBOARD_PASSWORD. Required for admin login.</summary>
    public string DashboardPassword { get; set; } = string.Empty;

    /// <summary>Env: ADMIN_SESSION_SECRET. HMAC key for session cookies.</summary>
    public string SessionSecret { get; set; } = string.Empty;

    /// <summary>Session lifetime in hours.</summary>
    public int SessionHours { get; set; } = 12;
}
