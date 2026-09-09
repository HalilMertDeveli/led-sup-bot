using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow.Knowledge;

namespace LedSupBot.Application.Workflow.Web;

public sealed class WebGroundingOptions
{
    public const string SectionName = "WebGrounding";
    public bool Enabled { get; set; } = true;
    public bool SearchOnlyWhenRequired { get; set; } = true;
    public int MaxAttemptsPerTurn { get; set; } = 1;
}

public enum InformationSourceRoute { NoSearch, WebSearch, RagAndWeb }

public sealed record WebSourceReference(string Title, string Uri);

public sealed record WebGroundingResult(
    bool Attempted,
    bool Used,
    bool Grounded,
    string? Reply,
    IReadOnlyList<WebSourceReference> Sources)
{
    public static WebGroundingResult NotAttempted { get; } = new(false, false, false, null, []);
    public static WebGroundingResult Failed { get; } = new(true, false, false, null, []);
}

public interface IWebGroundingService
{
    Task<WebGroundingResult> SearchAsync(string publicQuestion, CancellationToken cancellationToken = default);
}

public static partial class InformationSourceRoutingPolicy
{
    public static InformationSourceRoute Decide(
        string? message,
        ConversationIntent intent,
        KnowledgeGroundingResult rag,
        WebGroundingOptions options)
    {
        if (!options.Enabled || options.MaxAttemptsPerTurn < 1 || string.IsNullOrWhiteSpace(message))
            return InformationSourceRoute.NoSearch;

        var text = Normalize(message);
        if (ConversationSemantics.IsWaitAcknowledgement(message) || intent == ConversationIntent.Acknowledgement)
            return InformationSourceRoute.NoSearch;
        if (intent == ConversationIntent.CasualConversation)
            return InformationSourceRoute.NoSearch;
        if (intent == ConversationIntent.QuestionAboutCurrentStep)
            return InformationSourceRoute.NoSearch;

        var cardQuestion = TechnicalCardQuestionRegex().IsMatch(text);
        var requiresCurrent = CurrentInformationRegex().IsMatch(text) || ExplicitSearchRegex().IsMatch(text);
        if (intent == ConversationIntent.LedTroubleshooting &&
            !ExplicitSearchRegex().IsMatch(text) &&
            !CurrentProductStateRegex().IsMatch(text))
            return InformationSourceRoute.NoSearch;
        // Exact verified internal product knowledge remains authoritative unless the
        // customer explicitly asks for changing/current external information.
        if (rag.Level == GroundingLevel.GroundedExact && !CurrentProductStateRegex().IsMatch(text))
            return InformationSourceRoute.NoSearch;

        if (cardQuestion)
            return rag.Used ? InformationSourceRoute.RagAndWeb : InformationSourceRoute.WebSearch;
        if (!requiresCurrent) return InformationSourceRoute.NoSearch;

        return rag.Used ? InformationSourceRoute.RagAndWeb : InformationSourceRoute.WebSearch;
    }

    public static string BuildPublicQuery(string message)
    {
        var value = PhoneRegex().Replace(message, " ");
        value = AnyDeskRegex().Replace(value, " ");
        value = SecretRegex().Replace(value, " ");
        value = WindowsPathRegex().Replace(value, " ");
        value = PrivateIdentifierRegex().Replace(value, " ");
        value = WhitespaceRegex().Replace(value, " ").Trim();
        return value.Length <= 300 ? value : value[..300];
    }

    private static string Normalize(string value)
    {
        var decomposed = value.ToLower(new CultureInfo("tr-TR")).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c == 'ı' ? 'i' : c);
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"\b(bugun|simdi|su an|guncel|en son|en yeni|son model|yeni cikan|hala satiliyor|202[4-9]|dolar|euro|kur|hava)\b", RegexOptions.CultureInvariant)]
    private static partial Regex CurrentInformationRegex();
    [GeneratedRegex(@"\b(internetten|webde|google(?:'da|da)?|ara(?:r misin)?|kontrol et|bakabilir misin)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitSearchRegex();
    [GeneratedRegex(@"\b(firmware|surum|versiyon|yeni cikan|en son|en yeni|guncel|hala satiliyor)\b", RegexOptions.CultureInvariant)]
    private static partial Regex CurrentProductStateRegex();
    [GeneratedRegex(@"^(?=.*\b(?:kart|receiver|receiving card|sender card|alici kart|verici kart|controller|kontrolcu|processor|islemci|player|playbox|e\d+|5a-?75e|x\d+[a-z]?)\b)(?=.*(?:\?|kac|kapasite|cozunurluk|piksel|pixel|port|firmware|ne ise yar|destek)).*$", RegexOptions.CultureInvariant)]
    private static partial Regex TechnicalCardQuestionRegex();
    [GeneratedRegex(@"(?<!\d)(?:\+?90\s*)?0?5\d{2}[\s.()-]*\d{3}[\s.()-]*\d{2}[\s.()-]*\d{2}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();
    [GeneratedRegex(@"(?i)\banydesk\s*(?:adres(?:i)?|kod(?:u)?|id)?\s*[:#-]?\s*\d{8,12}\b", RegexOptions.CultureInvariant)]
    private static partial Regex AnyDeskRegex();
    [GeneratedRegex(@"(?i)\b(?:api[-_ ]?key|access[-_ ]?token|auth[-_ ]?token|bearer|token)\s*[:=]\s*[^\s;,]+", RegexOptions.CultureInvariant)]
    private static partial Regex SecretRegex();
    [GeneratedRegex(@"(?i)(?:[a-z]:\\|\\\\)[^\s\""'<>|]+", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPathRegex();
    [GeneratedRegex(@"(?i)\b(?:conversation|case|customer)[-_ ]?id\s*[:=]\s*[a-f0-9-]{8,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateIdentifierRegex();
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

public sealed class UnavailableWebGroundingService : IWebGroundingService
{
    public Task<WebGroundingResult> SearchAsync(string publicQuestion, CancellationToken cancellationToken = default) =>
        Task.FromResult(WebGroundingResult.Failed);
}
