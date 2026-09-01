using LedSupBot.Application.Abstractions;
using LedSupBot.Application.WhatsApp;
using LedSupBot.Domain.Enums;

namespace LedSupBot.UnitTests;

public class WhatsAppEventMapperTests
{
    [Theory]
    [InlineData(false, false, MessageSender.Customer)]
    [InlineData(true, true, MessageSender.Bot)]
    [InlineData(true, false, MessageSender.Technician)]
    public void MapSender_Uses_FromMe_And_BotOriginated(bool fromMe, bool botOriginated, MessageSender expected)
    {
        Assert.Equal(expected, WhatsAppEventMapper.MapSender(fromMe, botOriginated));
    }

    [Fact]
    public void MapConnectionStatus_Parses_Known_Values()
    {
        Assert.Equal(WhatsAppConnectionStatus.Connected, WhatsAppEventMapper.MapConnectionStatus("CONNECTED"));
        Assert.Equal(WhatsAppConnectionStatus.AuthenticationRequired, WhatsAppEventMapper.MapConnectionStatus("AUTHENTICATION_REQUIRED"));
        Assert.Equal(WhatsAppConnectionStatus.Disconnected, WhatsAppEventMapper.MapConnectionStatus("nope"));
    }

    [Fact]
    public void NormalizePhone_Strips_Plus()
    {
        Assert.Equal("905551112233", WhatsAppEventMapper.NormalizePhone("+905551112233"));
    }

    [Fact]
    public void ResolveCustomerAddress_Prefers_Lid_Jid_Over_Pseudo_Phone()
    {
        var address = WhatsAppEventMapper.ResolveCustomerAddress(new WhatsAppInboundEvent
        {
            RemoteJid = "207855059775610@lid",
            RemotePhoneNumber = "207855059775610",
        });

        Assert.Equal("207855059775610@lid", address);
    }

    [Fact]
    public void ResolveCustomerAddress_Uses_E164_When_Present()
    {
        var address = WhatsAppEventMapper.ResolveCustomerAddress(new WhatsAppInboundEvent
        {
            RemoteJid = "905551112233@s.whatsapp.net",
            RemotePhoneNumber = "905551112233",
        });

        Assert.Equal("905551112233", address);
    }
}
