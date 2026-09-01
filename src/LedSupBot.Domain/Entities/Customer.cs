using LedSupBot.Domain.Common;

namespace LedSupBot.Domain.Entities;

public class Customer : Entity
{
    public string PhoneNumber { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
}
