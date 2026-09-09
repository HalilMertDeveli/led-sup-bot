using System.Text.RegularExpressions;

namespace LedSupBot.Application.Workflow.Ai;

/// <summary>Lightweight heuristics for cost control — not authoritative parsing.</summary>
public static partial class TechnicalContentHeuristic
{
    public static bool MightContainTechnicalInfo(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Fast exclusion for common general knowledge / conversational questions
        if (IsClearlyGeneralChatOrQuestion(text))
        {
            return false;
        }

        return MightContainScreenInfo(text) ||
               MightContainReceiverInfo(text) ||
               MightContainPlayerInfo(text) ||
               MightContainHdmiInfo(text) ||
               MightContainConfigInfo(text) ||
               MightContainAnyDeskInfo(text) ||
               HasDimensionPattern(text) ||
               HasStandaloneCountPattern(text);
    }

    private static bool IsClearlyGeneralChatOrQuestion(string text)
    {
        var lower = text.ToLowerInvariant();
        if (Regex.IsMatch(lower, @"\b(?:başkent|baskent|hayvan|şaka|saka|ingilizce|yemek|film|şarkı|sarki|hava|yoruldum|moralim|nasılsın|nasilsin|ne\s+haber|naber)\b"))
        {
            return !Regex.IsMatch(lower, @"\b(?:ekran|receiver|reciver|config|anydesk|hdmi|playbox|processor)\b");
        }

        return false;
    }

    public static bool MightContainScreenInfo(string text)
    {
        var lower = text.ToLowerInvariant();

        // Guard against human/general chat False Positives
        if (Regex.IsMatch(lower, @"\b(?:boyum|kilom|yaşım|yasim|yaşındayım|nüfus|nufus|kişiyiz|kisiyiz|fiyat|ücret|ucret|lira|tl|hava|derece)\b"))
        {
            return Regex.IsMatch(lower, @"\b(?:ekran|panel|modül|modul|kasa)\b");
        }

        // If 'en' is followed by a superlative adjective (e.g. "en büyük", "en iyi"), it's Turkish superlative particle
        if (Regex.IsMatch(lower, @"\ben\s+(?:büyük|buyuk|küçük|kucuk|iyi|kötü|kotu|fazla|az|çok|cok|yüksek|yuksek|alçak|alcak|popüler|populer|pahalı|pahali|uzun|kısa|kisa|geniş|genis|dar|güzel|guzel)\b"))
        {
            return Regex.IsMatch(lower, @"\b(?:ekran|panel|ölçü|olcu|cm|metre|\d+\s*[x×*]\s*\d+)\b");
        }

        return Regex.IsMatch(
            lower,
            @"\b\d+\s*[x×*]\s*\d+\b|\bekran|\bölçü|\bolcu|\bcm\b|\bmetre|\bm\b|\byatay|\bdikey|\bdikeu|\bgenişlik|\bgenislik|\byükseklik|\byukseklik|\byukseklk|\ben\s*[:=]?\s*\d|\d\s*en\b|\bboy\s*[:=]?\s*\d|\d\s*boy\b|\bboyunda\b|\beninde\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static bool MightContainReceiverInfo(string text) =>
        Regex.IsMatch(
            text,
            @"\breceiver|\breciever|\breciver|\byeşil\s+kart|\byesil\s+kart|\bkart\b|\be120|\be80|\b5a75e|\b5a-75e|\be320|\bmrv|\brv908|\bdh7512",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool MightContainPlayerInfo(string text) =>
        Regex.IsMatch(
            text,
            @"\bplay\s*box|\bplaybox|\bpleybox|\bprocessor|\bprosesör|\bprossesor|\bişlemci|\bislemci|\boynatıcı|\boynatici",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool MightContainHdmiInfo(string text) =>
        Regex.IsMatch(
            text,
            @"\bhdmi\b|\bgiriş\b|\bgiris\b|\bçıkış\b|\bcikis\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool MightContainConfigInfo(string text)
    {
        if (Regex.IsMatch(text, @"\b(?:başkenti|hayvanı|şaka|ingilizce|yemek|film|şarkı|hava|yoruldum|moralim)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        return Regex.IsMatch(
            text,
            @"\bconfig|\bcobfig|\byazıcı\s*kabl|\byazici\s*kabl|\bbağlad|\bbaglad|\btakt|\btakıl|\btakil|\bkablo",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static bool MightContainAnyDeskInfo(string text) =>
        Regex.IsMatch(text, @"\banydesk|\benidesk|\buzak\s+bağlantı", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
        Regex.IsMatch(text, @"\b\d{3}[\s.-]?\d{3}[\s.-]?\d{3}\b", RegexOptions.CultureInvariant);

    private static bool HasDimensionPattern(string text) =>
        Regex.IsMatch(text, @"\b\d{1,4}\s*[x×*]\s*\d{1,4}\b", RegexOptions.CultureInvariant);

    private static bool HasStandaloneCountPattern(string text) =>
        Regex.IsMatch(text.Trim(), @"^(?:toplam\s+)?\d{1,3}\s*(?:tane|adet)?\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
