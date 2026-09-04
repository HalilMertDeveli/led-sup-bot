using System.Text.Json;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Knowledge;
using LedSupBot.Application.Media;
using LedSupBot.Application.Training;
using LedSupBot.Application.Workflow;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Application.Workflow.Knowledge;
using LedSupBot.Domain.Enums;

namespace LedSupBot.UnitTests;

public sealed class AiReplySnapshotFactoryTests
{
    [Fact]
    public void GeneralKnowledge_DoesNotFabricateRagUsage_AndRedactsSensitiveValues()
    {
        var context = Context();
        var grounding = new KnowledgeGroundingResult(
            Attempted: false,
            Succeeded: false,
            Used: false,
            DetectedModels: [],
            Sources: [],
            Context: null,
            ExactClaimNeedsModel: false,
            Level: GroundingLevel.GeneralKnowledge);

        var snapshot = AiReplySnapshotFactory.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "AnyDesk 123 456 789 ve token=super-secret",
            "Tamam, kontrol edelim.",
            context,
            ConversationStage.Start,
            PendingWorkflowFact.None,
            ConversationResponseAction.AnswerOnly,
            ConversationIntent.GeneralKnowledge,
            grounding,
            image: null,
            diagnosticContext: "none",
            geminiModel: "gemini-3.6-flash");

        Assert.Equal(AiTrainingSourceDecision.GeneralKnowledge, snapshot.SourceDecision);
        Assert.False(snapshot.RagAttempted);
        Assert.False(snapshot.RagUsed);
        Assert.True(snapshot.ContainsRedactions);
        Assert.DoesNotContain("123 456 789", snapshot.OriginalCustomerMessage);
        Assert.DoesNotContain("super-secret", snapshot.OriginalCustomerMessage);
        Assert.Equal(1, snapshot.TrainingSnapshotSchemaVersion);
    }

    [Fact]
    public void VisionAndRag_CapturesBoundedStructuredEvidenceWithoutChunkContent()
    {
        var source = new TechnicalKnowledgeResult(
            Content: "THIS FULL CHUNK MUST NOT BE COPIED",
            Score: .91,
            Document: @"C:\private\X20 manual.pdf",
            Model: "X20",
            PageStart: 12,
            PageEnd: 13,
            SectionTitle: "Output capacity",
            DocumentType: "manual",
            DocumentVersion: "2.1");
        var grounding = new KnowledgeGroundingResult(
            true, true, true, ["X20"], [source], "planner-only context", false,
            GroundingLevel.GroundedExact, .91, true, ["manual"], false);
        var rawImage = new TechnicalImageAnalysisResult(
            TechnicalImageCategory.ControllerLabel,
            DetectedText: "serial that must not be copied",
            CandidateManufacturer: "NovaStar",
            CandidateModels: ["X20"],
            CandidateSerialNumber: "private-serial",
            CandidateFirmwareVersion: null,
            CandidateHardwareVersion: null,
            CandidatePorts: [],
            VisibleConnections: ["HDMI connected"],
            VisibleIndicators: ["green LED"],
            Confidence: .95,
            Warnings: [],
            Source: ImageAnalysisSource.OcrAndVision,
            OcrAttempted: true,
            OcrSucceeded: true,
            VisionAttempted: true,
            VisionSucceeded: true);
        var image = new ValidatedImageAnalysis(rawImage, ImageCandidateStatus.Verified, ["X20"], null);

        var snapshot = AiReplySnapshotFactory.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Etiketteki X20 kaç piksel?", "X20 2.6 milyon piksel destekler.",
            Context(), ConversationStage.Start, PendingWorkflowFact.None,
            ConversationResponseAction.AnswerOnly, ConversationIntent.LedTechnicalQuestion,
            grounding, image, "none", "gemini-3.6-flash");

        Assert.Equal(AiTrainingSourceDecision.VisionAndRag, snapshot.SourceDecision);
        Assert.True(snapshot.RagUsed);
        Assert.True(snapshot.VisionUsed);
        Assert.True(snapshot.OcrUsed);
        Assert.True(snapshot.TechnicalClaimRequiresVerification);
        Assert.Contains("X20 manual.pdf", snapshot.KnowledgeSourceReferencesJson);
        Assert.DoesNotContain("C:\\private", snapshot.KnowledgeSourceReferencesJson);
        Assert.DoesNotContain("FULL CHUNK", snapshot.KnowledgeSourceReferencesJson);
        Assert.DoesNotContain("private-serial", snapshot.ImageContextSummary);
        Assert.Single(JsonSerializer.Deserialize<List<AiKnowledgeSourceSnapshot>>(snapshot.KnowledgeSourceReferencesJson)!);
    }

    [Fact]
    public void DiagnosticAndRag_IsDerivedFromActualDiagnosticsAndActualRagUse()
    {
        var grounding = new KnowledgeGroundingResult(
            true, true, true, ["E120"], [], null, false,
            GroundingLevel.GroundedGeneral, .7);

        var snapshot = AiReplySnapshotFactory.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Yaptım yine aynı.", "Şimdi farklı bir kablo deneyin.",
            Context(["Receiver: 4 adet E120"]), ConversationStage.AskConfigConnection,
            PendingWorkflowFact.ConfigConnection, ConversationResponseAction.AnswerOnly,
            ConversationIntent.LedTroubleshooting, grounding, null,
            "ActiveIssue=ekran çalışmıyor; PendingCheck=config-usb", "gemini-3.6-flash");

        Assert.Equal(AiTrainingSourceDecision.DiagnosticAndRag, snapshot.SourceDecision);
        Assert.Contains("ActiveIssue", snapshot.DiagnosticContextSnapshot);
    }

    [Fact]
    public void Unrelated_Question_Does_Not_Claim_Active_Diagnostic_State_As_Its_Source()
    {
        var grounding = new KnowledgeGroundingResult(
            false, false, false, [], [], null, false,
            GroundingLevel.GeneralKnowledge, 0);

        var snapshot = AiReplySnapshotFactory.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Receiver kart nedir?", "Receiver kart panel verisini sürer.",
            Context(), ConversationStage.AskConfigConnection, PendingWorkflowFact.ConfigConnection,
            ConversationResponseAction.AnswerOnly, ConversationIntent.GeneralKnowledge,
            grounding, null, "ActiveIssue=ekran titriyor; PendingCheck=güç-kaynağı", "gemini-3.6-flash");

        Assert.Equal(AiTrainingSourceDecision.GeneralKnowledge, snapshot.SourceDecision);
    }

    [Theory]
    [InlineData("Kapasite 2.6 milyon pikseldir.")]
    [InlineData("Çözünürlük 1920×1080 olarak ayarlanır.")]
    [InlineData("Koruma sınıfı IP65'tir.")]
    [InlineData("Parlaklık yüzde 80 olmalıdır.")]
    public void Common_Exact_Technical_Formats_Require_Verification(string response)
    {
        Assert.True(AiReplySnapshotFactory.ContainsPotentialTechnicalClaim(response));
    }

    [Fact]
    public void BackendState_Is_Derived_When_Response_Uses_A_Stored_Fact()
    {
        var grounding = new KnowledgeGroundingResult(
            false, false, false, [], [], null, false,
            GroundingLevel.GeneralKnowledge, 0);

        var snapshot = AiReplySnapshotFactory.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Ben kaç receiver kullanıyordum?", "4 adet E120 receiver kullanıyordunuz.",
            Context(["Receiver: 4 adet E120"]), ConversationStage.AskConfigConnection,
            PendingWorkflowFact.ConfigConnection, ConversationResponseAction.AnswerOnly,
            ConversationIntent.GeneralKnowledge, grounding, null, "none", "gemini-3.6-flash");

        Assert.Equal(AiTrainingSourceDecision.BackendState, snapshot.SourceDecision);
    }

    [Fact]
    public void Rag_Does_Not_Claim_BackendState_For_An_Unrelated_Stored_Fact()
    {
        var grounding = new KnowledgeGroundingResult(
            true, true, true, ["X20"], [], "bounded evidence", false,
            GroundingLevel.GroundedExact, .9, true);

        var snapshot = AiReplySnapshotFactory.Create(
            Guid.NewGuid(), Guid.NewGuid(), "X20 maksimum kaç piksel destekliyor?", "X20 için doğrulanmış değer belgededir.",
            Context(["Receiver: 4 adet E120"]), ConversationStage.AskConfigConnection,
            PendingWorkflowFact.ConfigConnection, ConversationResponseAction.AnswerOnly,
            ConversationIntent.LedTechnicalQuestion, grounding, null, "none", "gemini-3.6-flash");

        Assert.Equal(AiTrainingSourceDecision.Rag, snapshot.SourceDecision);
    }

    [Fact]
    public void Preferred_Response_Privacy_Filter_Removes_Secrets_Numbers_And_Local_Paths()
    {
        var safe = AiReplySnapshotFactory.SanitizePreferredResponse(
            @"AnyDesk 123 456 789, authorization=Bearer private-token, sk-abcdefghijklmnop ve C:\private\customer.txt",
            out var redacted);

        Assert.True(redacted);
        Assert.DoesNotContain("123 456 789", safe);
        Assert.DoesNotContain("private-token", safe);
        Assert.DoesNotContain("sk-abcdefghijklmnop", safe);
        Assert.DoesNotContain(@"C:\private", safe);
    }

    private static ConversationContext Context(IReadOnlyList<string>? knownFacts = null) => new()
    {
        ConversationId = Guid.NewGuid(),
        CurrentStage = ConversationStage.Start,
        PendingFact = PendingWorkflowFact.None,
        KnownFacts = knownFacts ?? [],
        MissingFacts = [],
        DeferredMissingFacts = [],
        BotMode = BotMode.Bot,
    };
}
