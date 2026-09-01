namespace LedSupBot.Application.Workflow.Ai;

public sealed class AiExtractionOptions
{
    public const string SectionName = "AiExtraction";

    /// <summary>Minimum confidence (0–1) before a slot is accepted from AI extraction.</summary>
    public double ConfidenceThreshold { get; set; } = 0.85;

    /// <summary>When true, deterministic parsers run first; AI supplements unresolved slots.</summary>
    public bool PreferDeterministicFirst { get; set; } = true;

    /// <summary>Configured model name for audit metadata (set from OpenAI options at startup).</summary>
    public string? ModelName { get; set; }
}
