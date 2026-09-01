namespace LedSupBot.Application.Workflow;

/// <summary>
/// Workflow engine configuration. Auto-reply defaults to false for live safety.
/// </summary>
public sealed class WorkflowOptions
{
    public const string SectionName = "Workflow";

    /// <summary>When false, workflow updates slots/state but does not send WhatsApp messages.</summary>
    public bool AutoReplyEnabled { get; set; }

    /// <summary>Optional path to config cable example image on disk.</summary>
    public string? ConfigExampleImagePath { get; set; }

    public NaturalResponseTimingOptions NaturalTiming { get; set; } = new();

    public InboundCoalescingOptions InboundCoalescing { get; set; } = new();
}

public sealed class NaturalResponseTimingOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Greeting / very short reply (seconds).</summary>
    public double GreetingMinSeconds { get; set; } = 1.5;

    public double GreetingMaxSeconds { get; set; } = 3.5;

    public int GreetingMaxChars { get; set; } = 60;

    /// <summary>Short technical reply (seconds).</summary>
    public double ShortMinSeconds { get; set; } = 2.5;

    public double ShortMaxSeconds { get; set; } = 6;

    public int ShortMaxChars { get; set; } = 120;

    /// <summary>Medium reply (seconds).</summary>
    public double MediumMinSeconds { get; set; } = 4;

    public double MediumMaxSeconds { get; set; } = 9;

    public int MediumMaxChars { get; set; } = 280;

    /// <summary>Long reply (seconds, capped by MaxDelaySeconds).</summary>
    public double LongMinSeconds { get; set; } = 6;

    public double LongMaxSeconds { get; set; } = 12;

    public double MaxDelaySeconds { get; set; } = 15;
}

public sealed class InboundCoalescingOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Wait for additional customer messages before running workflow.</summary>
    public double WindowSeconds { get; set; } = 3;
}
