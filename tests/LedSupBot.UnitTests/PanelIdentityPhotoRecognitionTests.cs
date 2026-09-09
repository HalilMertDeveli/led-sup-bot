using LedSupBot.Application.PanelFiles;

namespace LedSupBot.UnitTests;

public sealed class PanelIdentityPhotoRecognitionTests
{
    [Theory]
    [InlineData("P2.5\n1065S\n2012")]
    [InlineData("p2.5 1065s 2012")]
    public void Clear_label_is_complete_without_fuzzy_changes(string text)
    {
        var result = PanelLabelIdentityRecognizer.FromOcr(text);
        Assert.Equal(PanelLabelRecognitionStatus.CompleteHighConfidence, result.RecognitionStatus);
        Assert.Equal("P2.5", result.PixelPitchCandidate);
        Assert.Equal("1065S", result.PanelCodeCandidate);
        Assert.Equal("2012", result.SecondaryCodeCandidate);
        Assert.True(result.OcrUsed);
        Assert.False(result.VisionUsed);
    }

    [Fact]
    public void Uncertain_character_is_ambiguous_and_never_inferred()
    {
        var result = PanelLabelIdentityRecognizer.FromOcr("P2.5\n1065S\n201?");
        Assert.Equal(PanelLabelRecognitionStatus.Ambiguous, result.RecognitionStatus);
        Assert.Null(result.SecondaryCodeCandidate);
    }

    [Fact]
    public void Missing_pitch_stays_missing()
    {
        var result = PanelLabelIdentityRecognizer.FromOcr("1065S\n2012");
        Assert.Equal(PanelLabelRecognitionStatus.Partial, result.RecognitionStatus);
        Assert.Null(result.PixelPitchCandidate);
        Assert.Equal("1065S", result.PanelCodeCandidate);
        Assert.Equal("2012", result.SecondaryCodeCandidate);
    }

    [Fact]
    public void Multiple_labels_are_not_merged()
    {
        var result = PanelLabelIdentityRecognizer.FromOcr("P2.5 1065S 2012\nP3 XYZ1 2015");
        Assert.Equal(PanelLabelRecognitionStatus.MultipleLabels, result.RecognitionStatus);
        Assert.Null(result.PixelPitchCandidate);
    }

    [Fact]
    public void Ocr_and_vision_disagreement_is_conflicting()
    {
        var ocr = new PanelLabelIdentityObservation("P2.5", "1065S", "2012", PanelLabelRecognitionStatus.Partial, .9, .9, .5, "ocr", [], true, false);
        var vision = new PanelLabelIdentityObservation("P2.5", "1065S", "2013", PanelLabelRecognitionStatus.CompleteNeedsConfirmation, .9, .9, .9, "vision", [], false, true);
        var result = PanelLabelIdentityRecognizer.Merge(ocr, vision);
        Assert.Equal(PanelLabelRecognitionStatus.ConflictingEvidence, result.RecognitionStatus);
        Assert.Null(result.SecondaryCodeCandidate);
    }
}
