using LedSupBot.Application.WhatsApp;
using Xunit;

namespace LedSupBot.UnitTests;

public sealed class WhatsAppEventMapperTests
{
    [Fact]
    public void NormalizeWhatsAppAddress_strips_device_suffix_from_lid()
    {
        Assert.Equal("207855059775610@lid", WhatsAppEventMapper.NormalizeWhatsAppAddress("207855059775610:16@lid"));
    }
}
