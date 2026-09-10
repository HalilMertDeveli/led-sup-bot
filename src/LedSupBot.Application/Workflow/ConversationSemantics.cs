using System.Globalization;
using System.Text;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow.Context;

namespace LedSupBot.Application.Workflow;

public enum ConversationResponseAction
{
    WorkflowPrompt = 0,
    AnswerAndHoldWorkflow,
    ResumeWorkflow,
    ClarifyCurrentStep,
    RecoveryResponse,
    AnswerOnly,
}

public static class ConversationSemantics
{
    public sealed record IntentRoute(
        ConversationIntent PrimaryIntent,
        bool HasScreenSetup,
        bool HasFileRequest,
        bool HasTechnicalSupport);

    public static IntentRoute Route(string? message)
    {
        var text = Normalize(message);
        if (string.IsNullOrWhiteSpace(text))
            return new(ConversationIntent.Unclear, false, false, false);

        var file = ContainsAny(text, "dosya", "dosyalari", "config dosyasi", "rcvbp", ".hex", "firmware") &&
                   ContainsAny(text, "gonder", "yolla", " at", "atar misin", "atabilir misin", "lazim", "istiyorum");
        var setup = ContainsAny(text, "yeni ekran", "ekran kuracagiz", "kurulum yapacagiz", "ekrani ayarlam", "sistemi tanimla") ||
                    ContainsAny(text, "receiverlari bagladik", "receiverleri bagladik") && text.Contains("devam");
        var technical = ContainsAny(text, "neden", "calismiyor", "goruntu yok", "gelmiyor", "offline", "kirmizi", "beyaz kaldi",
            "yarisi", "titriyor", "renk bozuk", "lan isigi", "destekliyor", "maksimum", "kapasite");

        var primary = technical ? ConversationIntent.LedTechnicalSupport
            : file ? ConversationIntent.FileRequest
            : setup ? ConversationIntent.ScreenSetup
            : DetectLegacy(text);
        return new(primary, setup, file, technical);
    }

    public static ConversationIntent Detect(string? message)
    {
        var text = Normalize(message);
        if (string.IsNullOrWhiteSpace(text)) return ConversationIntent.Unclear;

        return Route(message).PrimaryIntent;
    }

    private static ConversationIntent DetectLegacy(string text)
    {

        if (ContainsAny(text, "devam", "nerede kalmistik", "kuruluma donelim", "receivera donelim", "receiver tarafina donelim"))
            return ConversationIntent.ResumeWorkflow;

        if (ContainsAny(text, "zaten soyledim", "zaten verdim", "verdim ya", "olcuyu verdim",
                "olcuyu az once yazdim", "az once verdim", "daha once soyledim", "zaten gonderdim",
                "niye tekrar soruyorsun", "ayni seyi niye", "ne alaka", "ben onu sormadim",
                "soruyordun", "yanlis anladin", "geri donelim", "onceki konuya donelim",
                "az once baska bir sey", "baska bir sey soruyordum", "oyle demedim", "onu kastetmedim",
                "kac kere soyle", "kac defa yaz", "ayni seyi tekrar", "yine ayni seyi", "beni anlamiyorsun"))
            return ConversationIntent.FlowRecovery;

        if (ContainsAny(text, "receiver kart hangisi", "receiver hangisi", "config kablosu nedir", "anydesk nedir",
                "nereye takacagim", "hangi kablo", "nereden bakacagim", "nereye bakmam", "hangi cihazdan bak",
                "neyi baglayacagim", "kablodan bahsediyor", "anlamadim"))
            return ConversationIntent.QuestionAboutCurrentStep;

        if (ContainsAny(text, "pardon", "degil", "yanlis yazdim"))
            return ConversationIntent.Correction;

        if (IsWaitAcknowledgement(text) || IsPhotoOffer(text) || IsShortAcknowledgement(text))
            return ConversationIntent.Acknowledgement;

        if (ContainsAny(text, "bir sey daha soracagim", "bir soru soracagim"))
            return ConversationIntent.CasualConversation;

        if (ContainsAny(text, "yarisi", "goruntu yok", "gelmiyor", "calismiyor", "bozuk", "titriyor", "renk bozuk"))
            return ConversationIntent.LedTroubleshooting;

        return ConversationIntent.Unclear;
    }

    public static ConversationResponseAction ResolveAction(ConversationIntent intent) => intent switch
    {
        ConversationIntent.LedTechnicalQuestion or ConversationIntent.LedTroubleshooting or ConversationIntent.LedTechnicalSupport => ConversationResponseAction.AnswerAndHoldWorkflow,
        ConversationIntent.QuestionAboutCurrentStep => ConversationResponseAction.ClarifyCurrentStep,
        ConversationIntent.ResumeWorkflow => ConversationResponseAction.ResumeWorkflow,
        ConversationIntent.FlowRecovery => ConversationResponseAction.RecoveryResponse,
        ConversationIntent.CasualConversation or ConversationIntent.GeneralKnowledge or ConversationIntent.Acknowledgement => ConversationResponseAction.AnswerOnly,
        ConversationIntent.FileRequest => ConversationResponseAction.AnswerOnly,
        _ => ConversationResponseAction.WorkflowPrompt,
    };

    public static ConversationIntent PreferBackendRecognized(ConversationIntent local, ConversationIntent ai) =>
        local == ConversationIntent.Unclear ? ai : local;

    public static bool IsWaitAcknowledgement(string? message)
    {
        var text = Normalize(message);
        return ContainsAny(text, "bekle", "bir saniye", "bakiyorum", "dur bak", "dur kontrol", "simdi bakiyorum");
    }

    public static bool IsPhotoOffer(string? message)
    {
        var text = Normalize(message);
        return (ContainsAny(text, "foto", "fotograf", "resim") &&
                ContainsAny(text, "atsam", "gondersem", "atayim", "olur mu", "bakabilir"));
    }

    public static bool IsShortAcknowledgement(string? message)
    {
        var text = Normalize(message).Trim();
        return text is "tamam" or "tamamdir" or "ok" or "peki" or "eyvallah" or "sagol" or "sag ol" or "tesekkurler" ||
               text.Length <= 40 && ContainsAny(text, "tesekkur", "sagol", "sag ol", "eyvallah");
    }

    public static bool IsCableClarification(string? message)
    {
        var text = Normalize(message);
        return ContainsAny(text, "hangi kablo", "kablodan bahsediyor", "neyi baglayacagim", "nereye takacagim");
    }

    public static bool IsLocationClarification(string? message)
    {
        var text = Normalize(message);
        return ContainsAny(text, "nereden bak", "nereye bak", "hangi cihazdan bak");
    }

    private static bool ContainsAny(string value, params string[] candidates) => candidates.Any(value.Contains);

    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.ToLower(new CultureInfo("tr-TR")).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c == 'ı' ? 'i' : c);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
