using LedSupBot.Application.Abstractions;
using LedSupBot.Domain.Entities;

namespace LedSupBot.Application.WhatsApp;

public interface IWhatsAppTestAllowlist
{
    bool IsEnabled { get; }

    bool HasConfiguredIdentity { get; }

    /// <summary>True when auto-reply automation (workflow, typing, outbound) may run.</summary>
    bool IsAutoReplyAllowed(
        WhatsAppInboundEvent inbound,
        string customerAddress,
        Customer? customer);

    /// <summary>Defense-in-depth check before outbound send.</summary>
    bool IsOutboundAllowed(string customerAddress, Customer? customer = null);

    /// <summary>Explicit check whether a contact is in the permanent exclusion list.</summary>
    bool IsExcluded(string customerAddress, Customer? customer = null, WhatsAppInboundEvent? inbound = null);
}

public sealed record WhatsAppTestModeStatus(bool Enabled, bool IdentityConfigured, bool Active);
