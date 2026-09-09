using LedSupBot.Application.CaseMemory;

namespace LedSupBot.UnitTests;

public sealed class CaseMemoryPrivacyTests
{
    [Fact]
    public void Sanitizer_removes_private_values_before_embedding()
    {
        var value = CaseMemoryPrivacy.Sanitize(
            @"Ara +90 555 123 45 67 AnyDesk adresi 123456789 token=secret C:\Users\customer\case.txt E120 yarım ekran");
        Assert.DoesNotContain("555", value);
        Assert.DoesNotContain("123456789", value);
        Assert.DoesNotContain("secret", value);
        Assert.DoesNotContain(@"C:\Users", value);
        Assert.Contains("E120 yarım ekran", value);
    }
}
