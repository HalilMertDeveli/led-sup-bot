namespace LedSupBot.Application.Workflow.Knowledge;

public interface ITechnicalKnowledgeProvider
{
    Task<string?> GetRelevantKnowledgeAsync(
        string customerUtterance,
        IReadOnlyList<string> knownFacts,
        CancellationToken cancellationToken = default);
}
