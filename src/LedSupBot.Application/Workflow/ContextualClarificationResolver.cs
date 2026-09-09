using LedSupBot.Application.Workflow.Context;

namespace LedSupBot.Application.Workflow;

public static class ContextualClarificationResolver
{
    public static string Resolve(HybridResponseComposeInput input)
    {
        var message = input.Inbound.TextContent;
        if (ConversationSemantics.IsCableClarification(message)) return ResolveCable(input);
        if (ConversationSemantics.IsLocationClarification(message)) return ResolveLocation(input);
        return BuildStepExplanation(input.PendingFact);
    }

    private static string ResolveCable(HybridResponseComposeInput input)
    {
        var primary = ConversationSemantics.Normalize(input.Context.LastBotQuestion);
        var fallback = RelevantRecentContext(input.Context);
        var evidence = string.IsNullOrWhiteSpace(primary) ? fallback : primary;

        if (input.EffectiveStage == Domain.Enums.ConversationStage.AskPanelLabelPhotos || ContainsAny(evidence, "panel etiket", "etiket fotograf"))
            return "Panelin arka tarafındaki etiketteki bilgilere bakabilirsiniz. İsterseniz etiketi fotoğraf olarak gönderin, ben bakayım.";

        if (ContainsAny(evidence, "hdmi", "processor", "video sinyal") ||
            string.IsNullOrWhiteSpace(evidence) && input.PendingFact == PendingWorkflowFact.ProcessorHdmi)
            return "HDMI kablosundan bahsediyorum. Bilgisayarın HDMI çıkışından processorün HDMI girişine gidecek.";

        if (ContainsAny(evidence, "config", "usb-b", "usb b", "yazici kablo") ||
            string.IsNullOrWhiteSpace(evidence) && input.PendingFact == PendingWorkflowFact.ConfigConnection)
            return "USB-A ↔ USB-B, yani yazıcı kablosundan bahsediyorum.";

        if (ContainsAny(evidence, "cat6", "data kablo", "receiverlar arasi", "receiver data"))
            return "Receiverlar arasındaki data/CAT6 kablosundan bahsediyorum.";

        if (!string.IsNullOrWhiteSpace(fallback) && ContainsAny(fallback, "cat6", "data kablo", "receiverlar arasi", "receiver data"))
            return "Receiverlar arasındaki data/CAT6 kablosundan bahsediyorum.";

        return "HDMI, config veya data kablosundan hangisini soruyorsunuz?";
    }

    private static string ResolveLocation(HybridResponseComposeInput input)
    {
        var primary = ConversationSemantics.Normalize(input.Context.LastBotQuestion);
        var fallback = RelevantRecentContext(input.Context);
        var evidence = string.IsNullOrWhiteSpace(primary) ? fallback : primary;

        if (ContainsAny(evidence, "receiver modeli", "receiver model", "kart modeli") ||
            string.IsNullOrWhiteSpace(evidence) && input.PendingFact == PendingWorkflowFact.ReceiverModel)
            return "Receiver kartın üzerindeki etikette veya model yazısında görürsünüz. Panelin arkasındaki yeşil kart.";

        if (ContainsAny(evidence, "online", "cevrimici", "bagli gorun"))
            return "Hangi yazılımda ve hangi cihazı kontrol ettiğinizi söyler misiniz? Menü adı uydurmadan oradan tarif edeyim.";

        if (ContainsAny(evidence, "player model", "oynatici model", "processor model", "cihaz modeli") ||
            string.IsNullOrWhiteSpace(evidence) && input.PendingFact == PendingWorkflowFact.PlayerType)
            return "Genelde cihazın önünde veya arkasındaki model etiketinde yazar. Fotoğrafını atarsanız da bakabiliriz.";

        return "Neye bakacağınızı soruyorsunuz?";
    }

    private static string RelevantRecentContext(ConversationContext context) => string.Join(" ", context.RecentMessages
        .Where(message => message.Role is "bot" or "technician")
        .Reverse()
        .Take(3)
        .Select(message => ConversationSemantics.Normalize(message.Content)));

    private static bool ContainsAny(string value, params string[] candidates) => candidates.Any(value.Contains);

    private static string BuildStepExplanation(PendingWorkflowFact fact) => fact switch
    {
        PendingWorkflowFact.PanelLabel => "Panelin arka tarafındaki ürün etiketindeki kodları arıyoruz. Etiketi net şekilde fotoğraf olarak gönderebilirsiniz.",
        PendingWorkflowFact.ReceiverModel => "Panelin arkasında data kablolarının bağlı olduğu receiver karttan bahsediyorum. Kartın üzerinde E120, E80 veya 5A75E gibi bir model yazabilir.",
        PendingWorkflowFact.ConfigConnection => "Kutudan çıkan kare uçlu USB-B yazıcı kablosunu cihaza, standart USB ucunu bilgisayara bağlamanız gerekiyor.",
        PendingWorkflowFact.AnyDesk => "AnyDesk, bilgisayara uzaktan bağlanabilmemiz için gereken program. Açınca görünen adresi paylaşabilirsiniz.",
        PendingWorkflowFact.ProcessorHdmi => "Bilgisayardan çıkan HDMI kablosunu processor üzerindeki HDMI IN girişine takmanız gerekiyor.",
        _ => "Sorduğum adımı biraz daha açık anlatayım; hangi kısmın anlaşılmadığını söylerseniz oradan devam edelim."
    };
}
