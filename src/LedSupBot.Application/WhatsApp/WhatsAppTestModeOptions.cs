namespace LedSupBot.Application.WhatsApp;

/// <summary>
/// Temporary gate restricting bot auto-reply to one configured private test identity.
/// Disable in production by setting Enabled=false.
/// </summary>
public sealed class WhatsAppTestModeOptions
{
    public const string SectionName = "WhatsAppTestMode";

    public bool Enabled { get; set; }

    /// <summary>Local test phone (Turkish or E.164). Never commit real values to Git.</summary>
    public string AllowedPhoneNumber { get; set; } = string.Empty;
}
