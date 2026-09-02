using LedSupBot.Application.Abstractions;
using LedSupBot.Application.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LedSupBot.UnitTests;

public sealed class WhatsAppTestAllowlistTests
{
    [Fact]
    public void Blocks_business_connected_phone_when_test_mode_enabled()
    {
        var allowlist = CreateAllowlist(enabled: true, allowedPhone: "05425192119");
        var inbound = new WhatsAppInboundEvent
        {
            RemoteJid = "905469124845@s.whatsapp.net",
            RemotePhoneNumber = "905469124845",
            FromMe = false,
        };

        Assert.False(allowlist.IsAutoReplyAllowed(inbound, "905469124845", customer: null));
        Assert.False(allowlist.IsOutboundAllowed("905469124845@s.whatsapp.net", customer: null));
    }

    [Fact]
    public void Blocks_business_self_lid_without_private_sender_proof()
    {
        var allowlist = CreateAllowlist(enabled: true, allowedPhone: "05425192119");
        var inbound = new WhatsAppInboundEvent
        {
            RemoteJid = "268040956571702@lid",
            RemotePhoneNumber = null,
            SenderPnJid = null,
            FromMe = false,
        };

        Assert.False(allowlist.IsAutoReplyAllowed(inbound, "268040956571702@lid", customer: null));
    }

    [Fact]
    public void Allows_matching_private_phone_when_test_mode_enabled()
    {
        var allowlist = CreateAllowlist(enabled: true, allowedPhone: "05425192119");
        var inbound = new WhatsAppInboundEvent
        {
            RemoteJid = "905425192119@s.whatsapp.net",
            RemotePhoneNumber = "905425192119",
            FromMe = false,
        };

        Assert.True(allowlist.IsAutoReplyAllowed(inbound, "905425192119", customer: null));
    }

    [Fact]
    public void Blocks_different_private_phone_when_test_mode_enabled()
    {
        var allowlist = CreateAllowlist(enabled: true, allowedPhone: "05425192119");
        var inbound = new WhatsAppInboundEvent
        {
            RemoteJid = "905551234567@s.whatsapp.net",
            RemotePhoneNumber = "905551234567",
            FromMe = false,
        };

        Assert.False(allowlist.IsAutoReplyAllowed(inbound, "905551234567", customer: null));
    }

    [Fact]
    public void Allows_lid_when_sender_pn_proves_test_phone()
    {
        var allowlist = CreateAllowlist(enabled: true, allowedPhone: "05425192119");
        var inbound = new WhatsAppInboundEvent
        {
            RemoteJid = "207855059775610@lid",
            RemotePhoneNumber = null,
            SenderPnJid = "905425192119@s.whatsapp.net",
            FromMe = false,
        };

        Assert.True(allowlist.IsAutoReplyAllowed(inbound, "207855059775610@lid", customer: null));
    }

    [Fact]
    public void Blocks_lid_without_identity_proof()
    {
        var allowlist = CreateAllowlist(enabled: true, allowedPhone: "05425192119");
        var inbound = new WhatsAppInboundEvent
        {
            RemoteJid = "207855059775610@lid",
            RemotePhoneNumber = null,
            SenderPnJid = null,
            FromMe = false,
        };

        Assert.False(allowlist.IsAutoReplyAllowed(inbound, "207855059775610@lid", customer: null));
    }

    [Fact]
    public void Disabled_test_mode_allows_all_private_identities()
    {
        var allowlist = CreateAllowlist(enabled: false, allowedPhone: "05425192119");
        var inbound = new WhatsAppInboundEvent
        {
            RemoteJid = "905551234567@s.whatsapp.net",
            RemotePhoneNumber = "905551234567",
            FromMe = false,
        };

        Assert.True(allowlist.IsAutoReplyAllowed(inbound, "905551234567", customer: null));
    }

    [Fact]
    public void Remembers_lid_identity_after_sender_pn_proof_for_outbound()
    {
        var allowlist = CreateAllowlist(enabled: true, allowedPhone: "05425192119");
        var inbound = new WhatsAppInboundEvent
        {
            RemoteJid = "207855059775610@lid",
            SenderPnJid = "905425192119@s.whatsapp.net",
            FromMe = false,
        };

        Assert.True(allowlist.IsAutoReplyAllowed(inbound, "207855059775610@lid", customer: null));
        Assert.True(allowlist.IsOutboundAllowed("207855059775610@lid", customer: null));
    }

    [Fact]
    public void Allows_lid_with_linked_whatsapp_lid_on_customer()
    {
        var allowlist = CreateAllowlist(enabled: true, allowedPhone: "05425192119");
        var customer = new LedSupBot.Domain.Entities.Customer
        {
            PhoneNumber = "905425192119",
            LinkedWhatsAppLid = "207855059775610@lid",
        };
        var inbound = new WhatsAppInboundEvent
        {
            RemoteJid = "207855059775610@lid",
            RemotePhoneNumber = null,
            SenderPnJid = null,
            FromMe = false,
        };

        Assert.True(allowlist.IsAutoReplyAllowed(inbound, "207855059775610@lid", customer));
    }

    [Fact]
    public void Blocks_group_jid_even_when_test_mode_enabled()
    {
        var allowlist = CreateAllowlist(enabled: true, allowedPhone: "05425192119");
        var inbound = new WhatsAppInboundEvent
        {
            RemoteJid = "1234567890-123456789@g.us",
            FromMe = false,
        };

        Assert.False(allowlist.IsAutoReplyAllowed(inbound, "1234567890-123456789@g.us", customer: null));
    }

    private static WhatsAppTestAllowlist CreateAllowlist(bool enabled, string allowedPhone)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new WhatsAppTestAllowlist(
            Options.Create(new WhatsAppTestModeOptions { Enabled = enabled, AllowedPhoneNumber = allowedPhone }),
            Options.Create(new WhatsAppAutomationOptions()),
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WhatsAppTestAllowlist>.Instance);
    }
}
