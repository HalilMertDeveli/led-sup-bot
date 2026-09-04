using LedSupBot.Application.Training;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;

namespace LedSupBot.UnitTests;

public sealed class AiTrainingEligibilityPolicyTests
{
    [Fact]
    public void Approved_Is_Sft_Eligible()
    {
        var result = AiTrainingEligibilityPolicy.Evaluate(
            AiTrainingFeedbackStatus.Approved,
            "Yanıt",
            null,
            technicalClaimRequiresVerification: false);

        Assert.Equal(AiTrainingEligibility.EligibleForSft, result.Eligibility);
        Assert.True(result.EligibleForSft);
        Assert.False(result.EligibleForPreference);
    }

    [Fact]
    public void Edited_With_Different_Preferred_Response_Is_Sft_And_Preference_Eligible()
    {
        var result = AiTrainingEligibilityPolicy.Evaluate(
            AiTrainingFeedbackStatus.Edited,
            "Receiver sayısını söyleyin.",
            "Tam olarak kaç receiver kullanıyorsunuz?",
            technicalClaimRequiresVerification: false);

        Assert.Equal(AiTrainingEligibility.EligibleForPreference, result.Eligibility);
        Assert.True(result.EligibleForSft);
        Assert.True(result.EligibleForPreference);
    }

    [Fact]
    public void Rejected_Without_Correction_Is_Not_A_Training_Target()
    {
        var result = AiTrainingEligibilityPolicy.Evaluate(
            AiTrainingFeedbackStatus.Rejected,
            "Yanlış yanıt",
            null,
            technicalClaimRequiresVerification: false);

        Assert.Equal(AiTrainingEligibility.Excluded, result.Eligibility);
        Assert.False(result.EligibleForSft);
        Assert.False(result.EligibleForPreference);
    }

    [Fact]
    public void Rejected_With_Correction_Preserves_Future_Preference_Pair()
    {
        var result = AiTrainingEligibilityPolicy.Evaluate(
            AiTrainingFeedbackStatus.Rejected,
            "Receiver kesin arızalı.",
            "Bunu net söylemek için önce test etmemiz gerekiyor.",
            technicalClaimRequiresVerification: false);

        Assert.Equal(AiTrainingEligibility.EligibleForPreference, result.Eligibility);
        Assert.True(result.EligibleForSft);
        Assert.True(result.EligibleForPreference);
    }

    [Fact]
    public void Unverified_Technical_Claim_Always_Needs_Review()
    {
        var result = AiTrainingEligibilityPolicy.Evaluate(
            AiTrainingFeedbackStatus.Approved,
            "X20 tam olarak 999999 piksel destekler.",
            null,
            technicalClaimRequiresVerification: true);

        Assert.Equal(AiTrainingEligibility.NeedsReview, result.Eligibility);
        Assert.False(result.EligibleForSft);
        Assert.False(result.EligibleForPreference);
    }

    [Fact]
    public void Snapshot_Schema_Starts_At_Version_One()
    {
        Assert.Equal(3, AiReplyContextSnapshot.CurrentSchemaVersion);
        Assert.Equal(3, new AiReplyContextSnapshot().TrainingSnapshotSchemaVersion);
    }

    [Theory]
    [InlineData(199, TrainingCollectionProgressPolicy.Collecting)]
    [InlineData(200, TrainingCollectionProgressPolicy.ReadyForDatasetReview)]
    [InlineData(299, TrainingCollectionProgressPolicy.ReadyForDatasetReview)]
    [InlineData(300, TrainingCollectionProgressPolicy.TuningCandidateReady)]
    [InlineData(500, TrainingCollectionProgressPolicy.TuningCandidateReady)]
    public void Collection_Milestones_Are_Centralized_And_Deterministic(int count, string expectedStatus)
    {
        var result = TrainingCollectionProgressPolicy.Evaluate(count, new TrainingCollectionOptions());

        Assert.Equal(count, result.TuningReady);
        Assert.Equal(200, result.FirstReviewThreshold);
        Assert.Equal(300, result.FirstTuningMin);
        Assert.Equal(500, result.FirstTuningTarget);
        Assert.Equal(expectedStatus, result.Status);
    }
}
