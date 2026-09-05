using LedSupBot.Application.PanelFiles;

namespace LedSupBot.UnitTests;

public sealed class PanelIdentityTextParserTests
{
    private readonly PanelIdentityTextParser _parser = new(new PanelIdentityNormalizer());

    [Fact]
    public void Multiple_distinct_text_identities_are_ambiguous()
    {
        var result = _parser.Parse("P2.5+1065S+2012 ve P3.91+AB12+2013");
        Assert.Equal(PanelIdentityTextStatus.AmbiguousIdentity, result.Status);
        Assert.Null(result.Identity);
    }

    [Theory]
    [InlineData("P2.5+1065S+2012")]
    [InlineData("p2.5+1065s+2012")]
    [InlineData("P2.5 + 1065S + 2012")]
    [InlineData("P2.5-1065S-2012")]
    [InlineData("P2.5 / 1065S / 2012")]
    [InlineData("P2.5 1065S 2012")]
    [InlineData("Etikette P2.5 + 1065S + 2012 yazıyor.")]
    [InlineData("Panel p2.5 1065s 2012")]
    [InlineData("Bizim panel P2.5, 1065S, 2012.")]
    public void Complete_variants_use_pf1_canonical_identity(string text)
    {
        var result = _parser.Parse(text);
        Assert.Equal(PanelIdentityTextStatus.CompleteIdentity, result.Status);
        Assert.Equal("P2.5-1065S-2012", result.Identity!.CanonicalKey);
    }

    [Theory]
    [InlineData("P2.5+1065S+2013", "P2.5-1065S-2013")]
    [InlineData("P2.6+1065S+2012", "P2.6-1065S-2012")]
    [InlineData("P2.5+1065+2012", "P2.5-1065-2012")]
    public void Neighboring_values_are_preserved_without_semantic_correction(string text, string expected)
    {
        var result = _parser.Parse(text);
        Assert.Equal(PanelIdentityTextStatus.CompleteIdentity, result.Status);
        Assert.Equal(expected, result.Identity!.CanonicalKey);
    }

    [Fact]
    public void Missing_pitch_remains_partial()
    {
        var result = _parser.Parse("1065S+2012");
        Assert.Equal(PanelIdentityTextStatus.PartialIdentity, result.Status);
        Assert.Null(result.Identity!.PixelPitch);
        Assert.Equal("1065S", result.Identity.PanelCode);
        Assert.Equal("2012", result.Identity.SecondaryCode);
    }

    [Fact]
    public void Missing_secondary_remains_partial()
    {
        var result = _parser.Parse("P2.5+1065S");
        Assert.Equal(PanelIdentityTextStatus.PartialIdentity, result.Status);
        Assert.Equal("P2.5", result.Identity!.PixelPitch);
        Assert.Equal("1065S", result.Identity.PanelCode);
        Assert.Null(result.Identity.SecondaryCode);
    }

    [Fact]
    public void Two_numeric_values_are_ambiguous()
    {
        Assert.Equal(PanelIdentityTextStatus.AmbiguousIdentity, _parser.Parse("P2.5+2012").Status);
    }

    [Theory]
    [InlineData("1920x1080")]
    [InlineData("5 volt")]
    [InlineData("HDMI 2")]
    [InlineData("Türkiye'de 81 il var")]
    [InlineData("320x160")]
    public void Unrelated_numbers_are_not_panel_identity(string text) =>
        Assert.Equal(PanelIdentityTextStatus.NoIdentity, _parser.Parse(text).Status);

    [Fact]
    public void Explicit_last_code_correction_is_bounded_partial_fact()
    {
        var result = _parser.Parse("Hayır son kod 2013.");
        Assert.Equal(PanelIdentityTextStatus.PartialIdentity, result.Status);
        Assert.Equal(PanelIdentitySource.OperatorCorrection, result.Source);
        Assert.Equal("2013", result.Identity!.SecondaryCode);
    }

    [Theory]
    [InlineData("Hayır, sonu 2013", PanelIdentityField.SecondaryCode, "2013")]
    [InlineData("1065S değil 1065R", PanelIdentityField.PanelCode, "1065R")]
    [InlineData("P2.5 değil P3", PanelIdentityField.PixelPitch, "P3")]
    public void Natural_field_corrections_only_target_the_intended_field(string text, PanelIdentityField field, string value)
    {
        var result = _parser.Parse(text);
        Assert.Equal(PanelIdentityTextStatus.PartialIdentity, result.Status);
        Assert.Equal(field, result.CorrectedField);
        Assert.Contains(value, new[] { result.Identity!.PixelPitch, result.Identity.PanelCode, result.Identity.SecondaryCode });
    }

    [Fact]
    public void Massive_malformed_input_is_safely_bounded() =>
        Assert.Equal(PanelIdentityTextStatus.NoIdentity, _parser.Parse(new string('x', 100_000)).Status);
}
