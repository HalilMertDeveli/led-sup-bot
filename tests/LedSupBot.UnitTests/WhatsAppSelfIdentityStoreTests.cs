using LedSupBot.Application.WhatsApp;
using Xunit;

namespace LedSupBot.UnitTests;

public sealed class WhatsAppSelfIdentityStoreTests
{
    [Fact]
    public void IsSelfJid_matches_normalized_business_lid()
    {
        var store = new WhatsAppSelfIdentityStore();
        store.SetSelfJids(["905469124845:92@s.whatsapp.net", "268040956571702:92@lid"]);

        Assert.True(store.IsSelfJid("268040956571702@lid"));
        Assert.False(store.IsSelfJid("207855059775610@lid"));
    }

    [Fact]
    public void IsSelfJid_ignores_phone_identity_without_domain()
    {
        var store = new WhatsAppSelfIdentityStore();
        store.SetSelfJids(["905469124845@s.whatsapp.net"]);

        Assert.False(store.IsSelfJid("905425192119"));
    }
}
