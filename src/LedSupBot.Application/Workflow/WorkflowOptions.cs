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
}
