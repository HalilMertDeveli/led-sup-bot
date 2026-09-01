using LedSupBot.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace LedSupBot.Application.Workflow.Ai;

public sealed class AiExtractionOrchestrator
{
    private readonly IAiService _ai;
    private readonly AiExtractionValidator _validator;
    private readonly ILogger<AiExtractionOrchestrator> _logger;

    public AiExtractionOrchestrator(
        IAiService ai,
        AiExtractionValidator validator,
        ILogger<AiExtractionOrchestrator> logger)
    {
        _ai = ai;
        _validator = validator;
        _logger = logger;
    }

    public async Task<ValidatedAiExtraction?> TryExtractAsync(
        string text,
        IReadOnlyDictionary<string, string?> knownSlots,
        DeterministicExtractionResult deterministic,
        CancellationToken cancellationToken)
    {
        if (!AiExtractionGate.ShouldInvokeAi(text, deterministic))
        {
            return null;
        }

        try
        {
            var extraction = await _ai.ExtractAsync(
                new AiExtractionRequest(text, LanguageHint: "tr", knownSlots),
                cancellationToken);

            var validated = _validator.Validate(extraction);
            _logger.LogInformation(
                "AI extraction completed intent={Intent} avgConfidence={Confidence}",
                validated.Intent,
                validated.AverageConfidence);

            return validated;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("AI extraction timed out; falling back to deterministic parsers");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI extraction failed; falling back to deterministic parsers");
            return null;
        }
    }
}
