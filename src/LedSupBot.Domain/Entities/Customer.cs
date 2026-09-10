using LedSupBot.Domain.Common;

namespace LedSupBot.Domain.Entities;

public class Customer : Entity
{
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Trusted WhatsApp LID channel (e.g. 123456789@lid) linked to this canonical customer.
    /// Set via senderPn proof or WhatsApp LID/PN mapping — never derived from LID digits alone.
    /// </summary>
    public string? LinkedWhatsAppLid { get; set; }

    public string? DisplayName { get; set; }

    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
}
