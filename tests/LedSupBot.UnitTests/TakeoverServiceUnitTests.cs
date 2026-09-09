using LedSupBot.Application.Takeover;
using LedSupBot.Domain.Enums;

namespace LedSupBot.UnitTests;

public sealed class TakeoverServiceUnitTests
{
    [Theory]
    [InlineData(BotMode.Bot, true)]
    [InlineData(BotMode.Paused, false)]
    [InlineData(BotMode.Human, false)]
    public void BotPolicy_Only_Bot_Allows_AutoReply(BotMode mode, bool allowed)
    {
        Assert.Equal(allowed, LedSupBot.Application.WhatsApp.ConversationBotPolicy.CanAutoReply(mode));
    }

    [Fact]
    public void TakeoverResult_Records_State()
    {
        var result = new TakeoverOperationResult(
            Found: true,
            Changed: true,
            PreviousMode: BotMode.Bot,
            CurrentMode: BotMode.Human,
            CurrentStage: ConversationStage.HumanHandoff,
            CurrentStatus: ConversationStatus.InHumanSupport);

        Assert.True(result.Found);
        Assert.Equal(BotMode.Human, result.CurrentMode);
        Assert.Equal(ConversationStage.HumanHandoff, result.CurrentStage);
    }
}
