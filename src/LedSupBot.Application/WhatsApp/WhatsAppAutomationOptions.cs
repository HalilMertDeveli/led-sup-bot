namespace LedSupBot.Application.WhatsApp;

public sealed class WhatsAppAutomationOptions
{
    public const string SectionName = "WhatsAppAutomation";

    /// <summary>
    /// List of phone numbers (in any format) that must NEVER receive automatic bot replies.
    /// Matched via canonical Turkish digits (e.g. 905xxxxxxxxx).
    /// </summary>
    public List<string> ExcludedPhoneNumbers { get; set; } = [];
}
