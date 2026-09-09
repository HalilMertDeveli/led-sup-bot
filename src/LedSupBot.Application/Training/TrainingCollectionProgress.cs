namespace LedSupBot.Application.Training;

public sealed class TrainingCollectionOptions
{
    public const string SectionName = "TrainingCollection";
    public int FirstReviewThreshold { get; set; } = 200;
    public int FirstTuningMin { get; set; } = 300;
    public int FirstTuningTarget { get; set; } = 500;
}

public sealed record TrainingCollectionProgress(
    int TuningReady,
    int FirstReviewThreshold,
    int FirstTuningMin,
    int FirstTuningTarget,
    string Status);

public static class TrainingCollectionProgressPolicy
{
    public const string Collecting = "COLLECTING";
    public const string ReadyForDatasetReview = "READY_FOR_DATASET_REVIEW";
    public const string TuningCandidateReady = "TUNING_CANDIDATE_READY";

    public static TrainingCollectionProgress Evaluate(int tuningReady, TrainingCollectionOptions options)
    {
        var review = Math.Max(1, options.FirstReviewThreshold);
        var tuningMin = Math.Max(review, options.FirstTuningMin);
        var tuningTarget = Math.Max(tuningMin, options.FirstTuningTarget);
        var count = Math.Max(0, tuningReady);
        var status = count >= tuningMin
            ? TuningCandidateReady
            : count >= review
                ? ReadyForDatasetReview
                : Collecting;
        return new(count, review, tuningMin, tuningTarget, status);
    }
}
