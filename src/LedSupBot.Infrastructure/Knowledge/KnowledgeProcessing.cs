using System.Security.Cryptography;
using System.Text.RegularExpressions;
using LedSupBot.Application.Knowledge;
using LedSupBot.Domain.Enums;

namespace LedSupBot.Infrastructure.Knowledge;

public sealed record ParsedKnowledgeMetadata(string Manufacturer, string ProductType, string ProductFamily, string Model, string DocumentType, string Version, string Language);

public static partial class KnowledgeProcessing
{
    public static string ResolveInsideRoot(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(candidate);
        if (!full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Document is outside the configured knowledge root.");
        return full;
    }
    public static string Sha256(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    public static ParsedKnowledgeMetadata ParseMetadata(string relativePath)
    {
        var name = Path.GetFileNameWithoutExtension(relativePath).Replace('\u00a0', ' ').Trim();
        var version = VersionRegex().Match(name).Value;
        var modelPart = Regex.Split(name, @"(?i)[ -]*Specification\b")[0].Trim(' ', '-');
        modelPart = Regex.Replace(modelPart, @"(?i)\s+V\d+(?:\.\d+)*$", "").Trim();
        var model = NormalizeModel(modelPart);
        var type = model.StartsWith("X", StringComparison.OrdinalIgnoreCase) || model.StartsWith("Z", StringComparison.OrdinalIgnoreCase) ? "Processor"
            : model.StartsWith("5A-", StringComparison.OrdinalIgnoreCase) || model.StartsWith("i", StringComparison.OrdinalIgnoreCase) ? "Sending Card" : "Splitter";
        var family = Regex.Match(model, @"^[A-Za-z]+|^5A").Value.ToUpperInvariant();
        return new("Colorlight", type, family, model, "Specification", version, "en");
    }
    public static string NormalizeModel(string value)
    {
        var s = Regex.Replace(value.Replace('_', ' '), @"\s+", " ").Trim();
        s = Regex.Replace(s, @"(?i)\bpro\b", "Pro");
        s = Regex.Replace(s, @"(?i)\bg(\d+)\b", "G$1");
        if (Regex.IsMatch(s, @"(?i)^x\d+[ems]$")) s = char.ToUpperInvariant(s[0]) + s[1..^1] + char.ToLowerInvariant(s[^1]);
        else if (Regex.IsMatch(s, @"(?i)^[a-z]+\d")) s = char.ToUpperInvariant(s[0]) + s[1..];
        return s;
    }
    public static ExtractionQuality Assess(IReadOnlyList<PdfPageText> pages, string expectedModel)
    {
        if (pages.Count == 0) return ExtractionQuality.Poor;
        var text = string.Join(' ', pages.Select(x => x.Text));
        var readable = text.Count(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c));
        var emptyRatio = pages.Count(x => string.IsNullOrWhiteSpace(x.Text)) / (double)pages.Count;
        if (text.Length < pages.Count * 40 || emptyRatio > .5 || readable < text.Length * .65) return ExtractionQuality.Poor;
        var modelSeen = text.Contains(expectedModel, StringComparison.OrdinalIgnoreCase);
        return modelSeen && text.Length >= pages.Count * 200 ? ExtractionQuality.Good : ExtractionQuality.Fair;
    }
    public static IReadOnlyList<(int Start, int End, string Section, string Content)> Chunk(IReadOnlyList<PdfPageText> pages, int target = 1800, int overlap = 180)
    {
        var output = new List<(int,int,string,string)>();
        foreach (var page in pages)
        {
            var clean = Regex.Replace(page.Text, @"\s+", " ").Trim(); if (clean.Length == 0) continue;
            var section = SectionRegex().Match(clean) is { Success: true } m ? m.Value.Trim() : $"Page {page.PageNumber}";
            for (var pos=0; pos<clean.Length; pos += Math.Max(1,target-overlap)) { var len=Math.Min(target,clean.Length-pos); output.Add((page.PageNumber,page.PageNumber,section,clean.Substring(pos,len))); if(pos+len>=clean.Length) break; }
        }
        return output;
    }
    [GeneratedRegex(@"(?i)\bV\d+(?:\.\d+)*\b")] private static partial Regex VersionRegex();
    [GeneratedRegex(@"(?i)\b(Overview|Features|Inputs?|Outputs?|Loading Capacity|Specifications?|Ports?|Installation)\b")] private static partial Regex SectionRegex();
}
