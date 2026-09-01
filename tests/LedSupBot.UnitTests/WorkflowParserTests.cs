using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Parsing;
using LedSupBot.Domain.Enums;
using Microsoft.Extensions.Options;

namespace LedSupBot.UnitTests;

public sealed class WorkflowParserTests
{
    [Theory]
    [InlineData("500x300 cm", 500, 300)]
    [InlineData("500 cm x 300 cm", 500, 300)]
    [InlineData("500 cm yatay, 300 cm dikey", 500, 300)]
    [InlineData("Yatay 500 cm dikey 300 cm", 500, 300)]
    [InlineData("Yatay 500 cm dikey 300 cm, 8 tane receiver kullandık", 500, 300)]
    public void ScreenSizeParser_Parses_Explicit_Cm_Variations(string text, decimal width, decimal height)
    {
        var result = ScreenSizeParser.Parse(text);

        Assert.True(result.ParsedAny);
        Assert.Equal(width, result.WidthCm);
        Assert.Equal(height, result.HeightCm);
        Assert.False(result.Ambiguous);
    }

    [Theory]
    [InlineData("5 metre x 3 metre", 500, 300)]
    [InlineData("5m x 3m", 500, 300)]
    [InlineData("5 metre yatay, 3 metre dikey", 500, 300)]
    public void ScreenSizeParser_Converts_Explicit_Meters_To_Cm(string text, decimal widthCm, decimal heightCm)
    {
        var result = ScreenSizeParser.Parse(text);

        Assert.True(result.ParsedAny);
        Assert.Equal(widthCm, result.WidthCm);
        Assert.Equal(heightCm, result.HeightCm);
        Assert.False(result.Ambiguous);
        Assert.False(result.NeedsMeterConfirmation);
    }

    [Theory]
    [InlineData("5x3")]
    [InlineData("500x300")]
    [InlineData("500 300")]
    public void ScreenSizeParser_Unitless_Dimensions_Are_Ambiguous(string text)
    {
        var result = ScreenSizeParser.Parse(text);

        Assert.True(result.ParsedAny);
        Assert.True(result.Ambiguous);
        Assert.Null(result.WidthCm);
        Assert.Null(result.HeightCm);
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

    [Theory]
    [InlineData("500x300 cm ve 8 receiver", 8)]
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

    [Fact]
    public void Multi_Slot_With_Explicit_Cm_Fills_Screen_And_Receiver()
    {
        const string text = "500 cm x 300 cm ve 8 receiver";
        var screen = ScreenSizeParser.Parse(text);
        Assert.Equal(500, screen.WidthCm);
        Assert.Equal(300, screen.HeightCm);

        Assert.True(ReceiverCountParser.TryParse(text, out var count));
        Assert.Equal(8, count);
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

public sealed class ScreenDimensionUnitPolicyTests
{
    private readonly AiExtractionValidator _validator = new(
        Options.Create(new AiExtractionOptions { ConfidenceThreshold = 0.85 }));

    [Fact]
    public void Ai_Validator_Converts_Explicit_Meters_To_Cm()
    {
        var result = _validator.Validate(new AiStructuredExtraction(
            CustomerMessageIntent.TechnicalSupport,
            new ScreenSlotCandidate(null, null, 5, 3, 0.96, false),
            null, null, null, []));

        Assert.NotNull(result.Screen);
        Assert.Equal(500, result.Screen!.WidthCm);
        Assert.Equal(300, result.Screen.HeightCm);
    }

    [Fact]
    public void Ai_Validator_Rejects_Unitless_5x3()
    {
        var result = _validator.Validate(new AiStructuredExtraction(
            CustomerMessageIntent.TechnicalSupport,
            new ScreenSlotCandidate(null, null, null, null, 0.6, true),
            null, null, null,
            ["unit ambiguous"]));

        Assert.Null(result.Screen);
    }

    [Fact]
    public void Ai_Validator_Accepts_Explicit_Cm()
    {
        var result = _validator.Validate(new AiStructuredExtraction(
            CustomerMessageIntent.TechnicalSupport,
            new ScreenSlotCandidate(500, 300, null, null, 0.98, false),
            null, null, null, []));

        Assert.Equal(500, result.Screen!.WidthCm);
        Assert.Equal(300, result.Screen.HeightCm);
    }
}
