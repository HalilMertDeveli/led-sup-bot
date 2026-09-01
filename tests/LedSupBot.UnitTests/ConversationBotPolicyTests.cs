using LedSupBot.Application.WhatsApp;
using LedSupBot.Domain.Enums;

namespace LedSupBot.UnitTests;

public class ConversationBotPolicyTests
{
    [Theory]
    [InlineData(BotMode.Bot, true)]
    [InlineData(BotMode.Paused, false)]
    [InlineData(BotMode.Human, false)]
    public void CanAutoReply_Only_When_BotMode_Bot(BotMode mode, bool expected)
    {
        Assert.Equal(expected, ConversationBotPolicy.CanAutoReply(mode));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    public void IsBotOriginated_Uses_Flag_Or_Persisted(bool flag, bool persisted, bool expected)
    {
        Assert.Equal(expected, ConversationBotPolicy.IsBotOriginated(flag, persisted));
    }
}
