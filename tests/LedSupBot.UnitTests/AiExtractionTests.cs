using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LedSupBot.UnitTests;

public sealed class AiExtractionValidatorTests
{
    private readonly AiExtractionValidator _validator = new(
        Options.Create(new AiExtractionOptions { ConfidenceThreshold = 0.85 }));

    [Fact]
    public void Validates_Explicit_Meters_Converted_To_Cm()
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
    public void Validates_500x300_Cm_From_Ai()
    {
        var result = _validator.Validate(new AiStructuredExtraction(
            CustomerMessageIntent.TechnicalSupport,
            new ScreenSlotCandidate(500, 300, null, null, 0.98, false),
            null, null, null, []));

        Assert.NotNull(result.Screen);
        Assert.True(result.Screen!.Complete);
        Assert.Equal(500, result.Screen.WidthCm);
        Assert.Equal(300, result.Screen.HeightCm);
    }

    [Fact]
    public void Validates_500_Cm_Yatay_300_Cm_Dikey()
    {
        var result = _validator.Validate(new AiStructuredExtraction(
            CustomerMessageIntent.TechnicalSupport,
            new ScreenSlotCandidate(500, 300, null, null, 0.95, false),
            null, null, null, []));

        Assert.True(result.Screen?.Complete);
    }

    [Fact]
    public void Meter_Units_Converted_To_Cm_Not_Confirmation()
    {
        var result = _validator.Validate(new AiStructuredExtraction(
            CustomerMessageIntent.TechnicalSupport,
            new ScreenSlotCandidate(null, null, 5, 3, 0.95, false),
            null, null, null, []));

        Assert.NotNull(result.Screen);
        Assert.Equal(500, result.Screen!.WidthCm);
        Assert.Equal(300, result.Screen.HeightCm);
        Assert.Empty(result.ClarificationPrompts);
    }

    [Fact]
    public void Validates_8_Yeşil_Kart()
    {
        var result = _validator.Validate(new AiStructuredExtraction(
            CustomerMessageIntent.TechnicalSupport,
            null,
            new ReceiverSlotCandidate(8, 0.97),
            null, null, []));

        Assert.Equal(8, result.ReceiverCount);
    }

    [Fact]
    public void Validates_8_Receiver_Typo_Reciever()
    {
        var result = _validator.Validate(new AiStructuredExtraction(
            CustomerMessageIntent.TechnicalSupport,
            null,
            new ReceiverSlotCandidate(8, 0.96),
            null, null, []));

        Assert.Equal(8, result.ReceiverCount);
    }

    [Fact]
    public void Multi_Slot_500x300_And_8_Receiver()
    {
        var result = _validator.Validate(new AiStructuredExtraction(
            CustomerMessageIntent.TechnicalSupport,
            new ScreenSlotCandidate(500, 300, null, null, 0.98, false),
            new ReceiverSlotCandidate(8, 0.97),
            null, null, []));

        Assert.True(result.Screen?.Complete);
        Assert.Equal(8, result.ReceiverCount);
    }

    [Fact]
    public void Informal_Turkish_Multi_Slot()
    {
        var result = _validator.Validate(new AiStructuredExtraction(
            CustomerMessageIntent.TechnicalSupport,
            new ScreenSlotCandidate(500, 300, null, null, 0.92, false),
            new ReceiverSlotCandidate(8, 0.91),
            null, null, []));

        Assert.True(result.Screen?.Complete);
        Assert.Equal(8, result.ReceiverCount);
    }

    [Fact]
    public void Ambiguous_5x3_Does_Not_Persist()
    {
        var result = _validator.Validate(new AiStructuredExtraction(
            CustomerMessageIntent.TechnicalSupport,
            new ScreenSlotCandidate(null, null, null, null, 0.6, true),
            null, null, null,
            ["screen unit ambiguous"]));

        Assert.Null(result.Screen);
        Assert.Contains(result.ClarificationPrompts, p => p.Contains("cm cinsinden"));
    }

    [Fact]
    public void Invalid_Partial_Screen_500_Only_Low_Confidence()
    {
        var result = _validator.Validate(new AiStructuredExtraction(
            CustomerMessageIntent.TechnicalSupport,
            new ScreenSlotCandidate(500, null, null, null, 0.5, false),
            null, null, null, []));

        Assert.Null(result.Screen);
        Assert.Contains(result.ClarificationPrompts, p => p.Contains("cm cinsinden"));
    }

    [Fact]
    public void Validates_AnyDesk_Address()
    {
        var result = _validator.Validate(new AiStructuredExtraction(
            CustomerMessageIntent.TechnicalSupport,
            null, null, null,
            new AnyDeskSlotCandidate("123456789", 0.99),
            []));

        Assert.Equal("123456789", result.AnyDeskAddress);
    }

    [Fact]
    public void Validates_Config_Confirmation()
    {
        var result = _validator.Validate(new AiStructuredExtraction(
            CustomerMessageIntent.Confirmation,
            null, null,
            new ConfigSlotCandidate(true, true, 0.94),
            null, []));

        Assert.True(result.ConfigConfirmed);
    }

    [Fact]
    public void Low_Confidence_Does_Not_Persist_Slots()
    {
        var result = _validator.Validate(new AiStructuredExtraction(
            CustomerMessageIntent.TechnicalSupport,
            new ScreenSlotCandidate(500, 300, null, null, 0.4, false),
            new ReceiverSlotCandidate(8, 0.3),
            null, null, []));

        Assert.Null(result.Screen);
        Assert.Null(result.ReceiverCount);
    }
}

public sealed class AiExtractionGateTests
{
    [Theory]
    [InlineData("5x3", true)]
    [InlineData("anydesk 123 45", true)]
    public void Gate_Controls_When_Ai_Is_Invoked(string text, bool expectInvoke)
    {
        var deterministic = DeterministicExtractionResult.Analyze(text);
        Assert.Equal(expectInvoke, AiExtractionGate.ShouldInvokeAi(text, deterministic));
    }

    [Fact]
    public void Gate_Skips_When_Deterministic_Fully_Resolves_Multi_Slot()
    {
        var text = "500 cm x 300 cm ve 8 receiver";
        var deterministic = DeterministicExtractionResult.Analyze(text);
        Assert.False(AiExtractionGate.ShouldInvokeAi(text, deterministic));
    }

    [Fact]
    public void Gate_Skips_When_Deterministic_Fully_Confident()
    {
        var text = "500 cm x 300 cm";
        var deterministic = DeterministicExtractionResult.Analyze(text);
        Assert.False(AiExtractionGate.ShouldInvokeAi(text, deterministic));
    }

    [Fact]
    public void Gate_Invokes_For_Unitless_Dimensions()
    {
        var text = "5x3";
        var deterministic = DeterministicExtractionResult.Analyze(text);
        Assert.True(AiExtractionGate.ShouldInvokeAi(text, deterministic));
    }

    [Fact]
    public void Gate_Invokes_For_Complex_Multi_Slot_Message()
    {
        var text = "Abi ekranımız 500'e 300, arkada 8 tane kart var";
        var deterministic = DeterministicExtractionResult.Analyze(text);
        Assert.True(AiExtractionGate.ShouldInvokeAi(text, deterministic));
    }
}

public sealed class AiExtractionOrchestratorTests
{
    [Fact]
    public async Task Orchestrator_Falls_Back_On_Timeout()
    {
        var ai = new ThrowingAiService(new TaskCanceledException());
        var orchestrator = new AiExtractionOrchestrator(
            ai,
            new AiExtractionValidator(Options.Create(new AiExtractionOptions())),
            NullLogger<AiExtractionOrchestrator>.Instance);

        var result = await orchestrator.TryExtractAsync(
            "5x3",
            new Dictionary<string, string?>(),
            DeterministicExtractionResult.Analyze("5x3"),
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Orchestrator_Falls_Back_On_Malformed_Response()
    {
        var ai = new ThrowingAiService(new InvalidOperationException("malformed"));
        var orchestrator = new AiExtractionOrchestrator(
            ai,
            new AiExtractionValidator(Options.Create(new AiExtractionOptions())),
            NullLogger<AiExtractionOrchestrator>.Instance);

        var result = await orchestrator.TryExtractAsync(
            "500 cm x 300 cm ve 8 receiver",
            new Dictionary<string, string?>(),
            DeterministicExtractionResult.Analyze("500 cm x 300 cm ve 8 receiver"),
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Orchestrator_Returns_Validated_On_Success()
    {
        var ai = new FakeAiService(new AiStructuredExtraction(
            CustomerMessageIntent.TechnicalSupport,
            new ScreenSlotCandidate(500, 300, null, null, 0.98, false),
            new ReceiverSlotCandidate(8, 0.97),
            null, null, []));

        var orchestrator = new AiExtractionOrchestrator(
            ai,
            new AiExtractionValidator(Options.Create(new AiExtractionOptions())),
            NullLogger<AiExtractionOrchestrator>.Instance);

        var result = await orchestrator.TryExtractAsync(
            "Abi ekranımız 500'e 300, arkada 8 tane kart var",
            new Dictionary<string, string?>(),
            DeterministicExtractionResult.Analyze("Abi ekranımız 500'e 300, arkada 8 tane kart var"),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(8, result!.ReceiverCount);
    }

    [Fact]
    public async Task Orchestrator_Skips_When_Gate_Closed()
    {
        var ai = new FakeAiService(new AiStructuredExtraction(
            CustomerMessageIntent.Greeting, null, null, null, null, []));
        var orchestrator = new AiExtractionOrchestrator(
            ai,
            new AiExtractionValidator(Options.Create(new AiExtractionOptions())),
            NullLogger<AiExtractionOrchestrator>.Instance);

        var result = await orchestrator.TryExtractAsync(
            "Merhaba",
            new Dictionary<string, string?>(),
            DeterministicExtractionResult.Analyze("Merhaba"),
            CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, ai.CallCount);
    }
}

internal sealed class FakeAiService(AiStructuredExtraction response) : IAiService
{
    public int CallCount { get; private set; }

    public Task<AiStructuredExtraction> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(response);
    }
}

internal sealed class ThrowingAiService(Exception ex) : IAiService
{
    public Task<AiStructuredExtraction> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
        throw ex;
}
