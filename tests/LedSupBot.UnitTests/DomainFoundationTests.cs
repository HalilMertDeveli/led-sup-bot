using LedSupBot.Domain.Enums;
using LedSupBot.Domain.ValueObjects;

namespace LedSupBot.UnitTests;

public class DomainFoundationTests
{
    [Fact]
    public void PhoneNumber_Trims_And_Requires_Value()
    {
        var phone = new PhoneNumber(" 905551112233 ");
        Assert.Equal("905551112233", phone.Value);
        Assert.Throws<ArgumentException>(() => new PhoneNumber(" "));
    }

    [Fact]
    public void ConversationStage_Includes_HumanHandoff_And_BotMode_Human()
    {
        Assert.Equal(9, (int)ConversationStage.HumanHandoff);
        Assert.Equal(2, (int)BotMode.Human);
        Assert.Equal(0, (int)BotMode.Bot);
    }
}
