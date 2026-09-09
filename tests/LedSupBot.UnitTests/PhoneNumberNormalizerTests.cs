using LedSupBot.Application.WhatsApp;
using Xunit;

namespace LedSupBot.UnitTests;

public sealed class PhoneNumberNormalizerTests
{
    [Theory]
    [InlineData("05425192119", "905425192119")]
    [InlineData("+905425192119", "905425192119")]
    [InlineData("905425192119", "905425192119")]
    [InlineData("5425192119", "905425192119")]
    public void TryNormalizeToCanonicalDigits_handles_turkish_formats(string input, string expected)
    {
        Assert.Equal(expected, PhoneNumberNormalizer.TryNormalizeToCanonicalDigits(input));
    }

    [Fact]
    public void TryNormalizeToCanonicalDigits_returns_null_for_lid_jid()
    {
        Assert.Null(PhoneNumberNormalizer.TryNormalizeToCanonicalDigits("207855059775610@lid"));
    }

    [Fact]
    public void CanonicalEquals_matches_equivalent_presentations()
    {
        Assert.True(PhoneNumberNormalizer.CanonicalEquals("05425192119", "+905425192119"));
        Assert.False(PhoneNumberNormalizer.CanonicalEquals("05425192119", "905551234567"));
    }
}
