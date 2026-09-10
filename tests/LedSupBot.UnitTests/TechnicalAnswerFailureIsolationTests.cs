using LedSupBot.Application.CaseMemory;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Application.Workflow.Knowledge;
using LedSupBot.Application.Workflow.TechnicalAnswers;
using LedSupBot.Application.Workflow.Web;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LedSupBot.UnitTests;

public sealed class TechnicalAnswerFailureIsolationTests
{
    [Fact]
    public async Task OptionalEvidenceFailures_DoNotAbortTechnicalAnswerContext()
    {
        var builder = new TechnicalAnswerContextBuilder(
            new TechnicalKnowledgeGroundingService([], NullLogger<TechnicalKnowledgeGroundingService>.Instance),
            new ThrowingTraining(),
            new ThrowingCustomerMemory(),
            new ThrowingCaseMemory(),
            new ThrowingWeb(),
            Options.Create(new WebGroundingOptions { Enabled = true, MaxAttemptsPerTurn = 1 }),
            NullLogger<TechnicalAnswerContextBuilder>.Instance);
        var conversation = new ConversationContext
        {
            CustomerId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            CurrentStage = ConversationStage.Start,
            StageLabel = "Start",
            BotMode = BotMode.Bot
        };

        var result = await builder.BuildAsync(new TechnicalAnswerContextRequest(
            conversation,
            new WorkflowSlotState(),
            "X4e güncel firmware sürümü nedir?",
            ConversationIntent.LedTechnicalQuestion,
            DiagnosticSymptom: "renk bozuk"));

        Assert.True(result.Training.Attempted);
        Assert.True(result.SameCustomerMemory.Attempted);
        Assert.True(result.CaseMemory.Attempted);
        Assert.True(result.Web.Attempted);
        Assert.False(result.Training.Used);
        Assert.False(result.SameCustomerMemory.Used);
        Assert.False(result.CaseMemory.Used);
        Assert.False(result.Web.Used);
        Assert.Equal(1, conversation.RequestBudget.WebCalls);
    }

    private sealed class ThrowingTraining : IApprovedTrainingExampleRetriever
    {
        public Task<ApprovedTrainingSearchResult> SearchAsync(string question, ConversationIntent intent,
            IReadOnlyList<string> models, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("training unavailable");
    }

    private sealed class ThrowingCustomerMemory : ISameCustomerConversationMemory
    {
        public Task<SameCustomerMemoryResult> SearchAsync(Guid customerId, Guid currentConversationId, string question,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("memory unavailable");
    }

    private sealed class ThrowingWeb : IWebGroundingService
    {
        public Task<WebGroundingResult> SearchAsync(string publicQuestion, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("web unavailable");
    }

    private sealed class ThrowingCaseMemory : IVerifiedCaseMemoryService
    {
        public Task<CaseMemorySearchResult> SearchAsync(string symptom, string? deviceModel,
            IReadOnlyCollection<string> completedChecks, CancellationToken ct = default) =>
            throw new InvalidOperationException("case memory unavailable");

        public Task<VerifiedSupportCaseMemory?> CreateDraftAsync(Guid caseId, CaseMemoryEdit? edit,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<VerifiedSupportCaseMemory?> UpdateDraftAsync(Guid id, CaseMemoryEdit edit,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<VerifiedSupportCaseMemory?> VerifyAsync(Guid id, string operatorReference,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<VerifiedSupportCaseMemory?> SetStatusAsync(Guid id, VerifiedCaseMemoryStatus status,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<VerifiedSupportCaseMemory>> ListAsync(VerifiedCaseMemoryStatus? status,
            string? model, string? category, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
