using LedSupBot.Application.WhatsApp;
using Xunit;

namespace LedSupBot.UnitTests;

public sealed class WhatsAppOutboundDestinationResolverTests
{
    [Fact]
    public void NormalizeWhatsAppAddress_preserves_lid_domain()
    {
        Assert.Equal("268040956571702@lid", WhatsAppEventMapper.NormalizeWhatsAppAddress("268040956571702:91@lid"));
    }

    [Theory]
    [InlineData("207855059775610@lid", "lid")]
    [InlineData("905425192119@s.whatsapp.net", "s.whatsapp.net")]
    public void Destination_type_detection(string address, string expectedType)
    {
        var type = WhatsAppEventMapper.IsLidJid(address)
            ? "lid"
            : WhatsAppEventMapper.IsGroupJid(address)
                ? "g.us"
                : "s.whatsapp.net";

        Assert.Equal(expectedType, type);
    }

    [Fact]
    public void Exact_route_preserved_when_lid_matches()
    {
        var requested = "268040956571702:91@lid";
        var resolved = WhatsAppEventMapper.NormalizeWhatsAppAddress(requested);
        Assert.Equal("268040956571702@lid", resolved);
        Assert.True(
            string.Equals(
                WhatsAppEventMapper.NormalizeWhatsAppAddress(requested),
                resolved,
                StringComparison.OrdinalIgnoreCase));
    }
}
