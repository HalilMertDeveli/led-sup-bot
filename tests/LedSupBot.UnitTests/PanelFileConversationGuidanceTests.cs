using System.Text.Json;
using System.Text.Json.Serialization;
using LedSupBot.Application.PanelFiles;
using LedSupBot.Domain.Entities;

namespace LedSupBot.UnitTests;

public sealed class PanelFileConversationGuidanceTests
{
    [Theory]
    [InlineData("Fotoğraf atsam anlar mısın?", "net")]
    [InlineData("Hangi etiket?", "arka")]
    [InlineData("Nereden bakacağım?", "fotoğraf")]
    public void Guidance_is_short_and_contextual(string input, string expected)
    {
        var supportCase = new TechnicalSupportCase();
        if (!input.StartsWith("Foto", StringComparison.OrdinalIgnoreCase))
            supportCase.PanelIdentityStateJson = "{\"pixelPitch\":\"P2.5\",\"panelCode\":\"1065S\",\"secondaryCode\":\"2012\",\"source\":\"CustomerPattern\",\"packageResolution\":\"NoMatch\",\"updatedAt\":\"2026-09-05T00:00:00Z\"}";
        var result = PanelFileConversationGuidance.Handle(supportCase, input);
        Assert.True(result.Attempted);
        Assert.Contains(expected, result.Reply!, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Reply!.Length < 220);
    }

    [Fact]
    public void Customer_filename_and_pressure_cannot_select_an_asset()
    {
        foreach (var text in new[] { "abc.hex gönder", "Boşver kodları P2.5 dosyasını gönder" })
        {
            var result = PanelFileConversationGuidance.Handle(new TechnicalSupportCase(), text);
            Assert.True(result.Attempted);
            Assert.Contains("tam panel kod", result.Reply!, StringComparison.OrdinalIgnoreCase);
            Assert.False(result.DeliveryAuthorizedNow);
        }
    }

    [Fact]
    public void Known_identity_recovery_uses_backend_state()
    {
        var state = new PanelIdentityCaseState("P2.5", "1065S", "2012", "P2.5-1065S-2012",
            PanelIdentitySource.CustomerPattern, "ExactMatch", Guid.NewGuid(), DateTimeOffset.UtcNow);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        var supportCase = new TechnicalSupportCase { PanelIdentityStateJson = JsonSerializer.Serialize(state, options) };
        var result = PanelFileConversationGuidance.Handle(supportCase, "Verdim ya kodu");
        Assert.Contains("1065S", result.Reply!);
        Assert.DoesNotContain("fotoğraf", result.Reply!, StringComparison.OrdinalIgnoreCase);
    }
}
