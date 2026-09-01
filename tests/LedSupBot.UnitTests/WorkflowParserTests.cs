using LedSupBot.Application.Workflow;
using LedSupBot.Application.Workflow.Parsing;
using LedSupBot.Domain.Enums;

namespace LedSupBot.UnitTests;

public sealed class WorkflowParserTests
{
    [Theory]
    [InlineData("500x300", 500, 300)]
    [InlineData("500 300", 500, 300)]
    [InlineData("500 cm yatay, 300 cm dikey", 500, 300)]
    [InlineData("Yatay 500 dikey 300", 500, 300)]
    [InlineData("Yatay 500 cm dikey 300 cm, 8 tane receiver kullandık", 500, 300)]
    public void ScreenSizeParser_Parses_Common_Variations(string text, decimal width, decimal height)
    {
        var result = ScreenSizeParser.Parse(text);

        Assert.True(result.ParsedAny);
        Assert.Equal(width, result.WidthCm);
        Assert.Equal(height, result.HeightCm);
        Assert.False(result.NeedsMeterConfirmation);
        Assert.False(result.Ambiguous);
    }

    [Fact]
    public void ScreenSizeParser_Only_Width_Supplied()
    {
        var result = ScreenSizeParser.Parse("Yatay 500 cm");

        Assert.True(result.ParsedAny);
        Assert.Equal(500, result.WidthCm);
        Assert.Null(result.HeightCm);
    }

    [Fact]
    public void ScreenSizeParser_Only_Height_Supplied()
    {
        var result = ScreenSizeParser.Parse("Dikey 300 cm");

        Assert.True(result.ParsedAny);
        Assert.Null(result.WidthCm);
        Assert.Equal(300, result.HeightCm);
    }

    [Fact]
    public void ScreenSizeParser_Ambiguous_Units_Require_Confirmation()
    {
        var result = ScreenSizeParser.Parse("5 metre x 3 metre");

        Assert.True(result.NeedsMeterConfirmation);
        Assert.False(result.Ambiguous);
    }

    [Theory]
    [InlineData("500x300 ve 8 receiver", 8)]
    [InlineData("8", 8)]
    [InlineData("8 tane", 8)]
    [InlineData("8 adet receiver", 8)]
    public void ReceiverCountParser_Parses_Valid_Counts(string text, int expected)
    {
        Assert.True(ReceiverCountParser.TryParse(text, out var count));
        Assert.Equal(expected, count);
    }

    [Theory]
    [InlineData("sıfır receiver")]
    [InlineData("receiver yok")]
    [InlineData("on tane")]
    public void ReceiverCountParser_Rejects_Invalid_Counts(string text)
    {
        Assert.False(ReceiverCountParser.TryParse(text, out _));
    }

    [Theory]
    [InlineData("500x300")]
    [InlineData("500x300 ve 8 receiver")]
    [InlineData("Yatay 500 cm dikey 300 cm, 8 tane receiver kullandık")]
    public void Multi_Slot_Message_Fills_Screen_And_Receiver(string text)
    {
        var screen = ScreenSizeParser.Parse(text);
        Assert.True(screen.WidthCm > 0 && screen.HeightCm > 0);

        var hasReceiver = ReceiverCountParser.TryParse(text, out var count);
        if (text.Contains("receiver", StringComparison.OrdinalIgnoreCase))
        {
            Assert.True(hasReceiver);
            Assert.Equal(8, count);
        }
    }

    [Fact]
    public void StageResolver_Skips_Completed_Slots()
    {
        var slots = new WorkflowSlotState
        {
            WidthCm = 500,
            HeightCm = 300,
            ReceiverCount = 8,
        };

        var stage = WorkflowStageResolver.ResolveEffectiveStage(slots, configExampleAssetAvailable: false, configExampleStepCompleted: true);

        Assert.Equal(ConversationStage.AskPanelLabelPhotos, stage);
    }

    [Fact]
    public void AnyDeskParser_Accepts_Nine_Digit_Address()
    {
        Assert.True(AnyDeskParser.TryParse("123 456 789", out var address));
        Assert.Equal("123456789", address);
    }

    [Fact]
    public void ConfigConfirmationParser_Accepts_Common_Confirmations()
    {
        Assert.Equal(ConfigConfirmationResult.Confirmed, ConfigConfirmationParser.Parse("evet"));
        Assert.Equal(ConfigConfirmationResult.Confirmed, ConfigConfirmationParser.Parse("bağladım"));
    }
}
