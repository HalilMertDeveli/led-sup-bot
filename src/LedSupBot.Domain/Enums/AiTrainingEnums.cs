namespace LedSupBot.Domain.Enums;

public enum AiTrainingFeedbackStatus
{
    Approved = 0,
    Edited = 1,
    Rejected = 2,
    Excluded = 3,
}

public enum AiTrainingEligibility
{
    EligibleForSft = 0,
    EligibleForPreference = 1,
    NeedsReview = 2,
    Excluded = 3,
}

public enum AiTrainingSourceDecision
{
    GeneralKnowledge = 0,
    BackendState = 1,
    DiagnosticState = 2,
    Rag = 3,
    Vision = 4,
    VisionAndRag = 5,
    BackendAndRag = 6,
    DiagnosticAndRag = 7,
    Deterministic = 8,
    Mixed = 9,
}

public enum AiTrainingFeedbackReason
{
    Tone = 0,
    TooLong = 1,
    TooShort = 2,
    RepeatedQuestion = 3,
    WrongWorkflow = 4,
    WrongContext = 5,
    WrongTechnicalInformation = 6,
    WrongSource = 7,
    Hallucination = 8,
    ShouldUseRag = 9,
    ShouldNotUseRag = 10,
    WrongDiagnosticStep = 11,
    DidNotUnderstandCustomer = 12,
    TooRobotic = 13,
    Other = 14,
}
