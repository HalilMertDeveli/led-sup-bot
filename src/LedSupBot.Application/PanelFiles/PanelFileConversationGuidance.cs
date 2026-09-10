using System.Text.Json;
using System.Text.Json.Serialization;
using LedSupBot.Application.Workflow;
using LedSupBot.Domain.Entities;

namespace LedSupBot.Application.PanelFiles;

/// <summary>Deterministic conversational affordances around backend-owned panel state.</summary>
public static class PanelFileConversationGuidance
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static PanelIdentityTurnResult Handle(TechnicalSupportCase supportCase, string? customerText)
    {
        var text = ConversationSemantics.Normalize(customerText);
        var identity = Read<PanelIdentityCaseState>(supportCase.PanelIdentityStateJson);
        var delivery = Read<PanelFileDeliveryState>(supportCase.PanelFileDeliveryStateJson);

        if (ConversationSemantics.IsPhotoOffer(customerText))
            return new(true, "Evet, etiketi net görecek şekilde gönderin.");

        if (identity is not null && (text.Contains("hangi etiket") || text.Contains("nereden bak") || text.Contains("nerede yazar")))
            return new(true, "Panelin arka tarafındaki ürün etiketindeki bilgilere bakabilirsiniz. İsterseniz etiketi fotoğraf olarak gönderin, ben bakayım.");

        if (text.Contains("yanlis anlad") && identity is not null)
        {
            var values = string.Join(", ", new[] { identity.PixelPitch, identity.PanelCode, identity.SecondaryCode }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return new(true, values.Length == 0 ? "Hangi panel bilgisini yanlış anladım?" : $"Hangi bilgi yanlış: {values}?");
        }

        if (text.Contains("verdim ya") || text.Contains("zaten verdim"))
        {
            if (identity?.CanonicalKey is not null)
                return new(true, $"Evet, {identity.PixelPitch} + {identity.PanelCode} + {identity.SecondaryCode} kayıtlı. Buradan devam ediyorum.");
            if (identity is not null)
            {
                var missing = identity.PixelPitch is null ? "pitch" : identity.PanelCode is null ? "panel kodu" : "son kod";
                return new(true, $"Verdiğiniz bilgileri aldım; yalnızca {missing} eksik görünüyor.");
            }
        }

        if ((text.Contains("dosyalari tekrar gonder") || text.Contains("dosyayi tekrar gonder") || text.Contains("yeniden gonder")) &&
            identity?.CanonicalKey is not null)
            return new(true, "Dosyaları tekrar göndermeden önce güvenlik kontrolünü yeniliyorum.", "{}", true, true);

        if ((text.Contains("bosver") && text.Contains("gonder")) ||
            (text.Contains(".hex") && text.Contains("gonder") && identity?.CanonicalKey is null))
            return new(true, "Doğru dosyayı göndermek için tam panel kodunu netleştirmem gerekiyor. Etiketi fotoğraf olarak atabilirsiniz.");

        if (text.Contains("gibi") && text.Any(char.IsDigit) && identity?.CanonicalKey is null)
            return new(true, "Koddan emin olmadan dosya seçemem. Etiketi kontrol edip net kodu yazabilir veya fotoğrafını gönderebilirsiniz.");

        var route = ConversationSemantics.Route(customerText);
        if (route.HasFileRequest && identity?.CanonicalKey is null && !text.Any(char.IsDigit))
            return new(true, "Dosyalar\u0131 kontrol edebilmem i\u00e7in klas\u00f6r/model ad\u0131n\u0131 yazar m\u0131s\u0131n\u0131z? \u00d6rnek: P2.5+1065S+2012");

        if ((text.Contains("nerede kalmistik") || text == "devam") && delivery is not null)
        {
            if (delivery.CompletedAt is not null) return new(true, "Dosya gönderimi tamamlanmıştı.");
            if (delivery.FirmwareMessageId is not null)
                return new(true, "HEX dosyası gönderildi; diğer dosyanın gönderimi tamamlanmamış görünüyor.");
        }
        return new(false);
    }

    private static T? Read<T>(string? json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch (JsonException) { return default; }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
