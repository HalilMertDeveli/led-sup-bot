namespace LedSupBot.Application.WhatsApp;

public interface IWhatsAppSelfIdentityStore
{
    IReadOnlyCollection<string> SelfJids { get; }

    void SetSelfJids(IEnumerable<string?> jids);

    bool IsSelfJid(string? jidOrAddress);
}

public sealed class WhatsAppSelfIdentityStore : IWhatsAppSelfIdentityStore
{
    private readonly HashSet<string> _selfJids = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> SelfJids => _selfJids;

    public void SetSelfJids(IEnumerable<string?> jids)
    {
        _selfJids.Clear();
        foreach (var jid in jids)
        {
            if (string.IsNullOrWhiteSpace(jid) || !jid.Contains('@', StringComparison.Ordinal))
            {
                continue;
            }

            _selfJids.Add(WhatsAppEventMapper.NormalizeWhatsAppAddress(jid));
        }
    }

    public bool IsSelfJid(string? jidOrAddress)
    {
        if (string.IsNullOrWhiteSpace(jidOrAddress) || _selfJids.Count == 0)
        {
            return false;
        }

        if (!jidOrAddress.Contains('@', StringComparison.Ordinal))
        {
            return false;
        }

        return _selfJids.Contains(WhatsAppEventMapper.NormalizeWhatsAppAddress(jidOrAddress));
    }
}
