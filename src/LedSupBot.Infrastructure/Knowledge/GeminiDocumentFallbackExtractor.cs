using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Text.Json;
using LedSupBot.Application.Knowledge;
using LedSupBot.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace LedSupBot.Infrastructure.Knowledge;

public sealed class GeminiDocumentFallbackExtractor : IDocumentFallbackExtractor
{
    private readonly HttpClient _http;
    private readonly GeminiOptions _gemini;
    private readonly FallbackExtractionOptions _options;

    public GeminiDocumentFallbackExtractor(HttpClient http, IOptions<GeminiOptions> gemini, IOptions<KnowledgeOptions> knowledge)
    { _http=http; _gemini=gemini.Value; _options=knowledge.Value.FallbackExtraction; }

    public async Task<PdfExtractionResult?> ExtractAsync(string path, CancellationToken cancellationToken)
    {
        var useGemini=string.Equals(_options.Provider,"Gemini",StringComparison.OrdinalIgnoreCase) || string.Equals(_options.Provider,"GeminiThenLocalOcr",StringComparison.OrdinalIgnoreCase);
        var useLocal=string.Equals(_options.Provider,"LocalOcr",StringComparison.OrdinalIgnoreCase) || string.Equals(_options.Provider,"GeminiThenLocalOcr",StringComparison.OrdinalIgnoreCase);
        if (!useGemini && !useLocal) throw new InvalidOperationException("Knowledge fallback provider must be Gemini, LocalOcr, or GeminiThenLocalOcr.");
        if (useGemini && (string.IsNullOrWhiteSpace(_options.Model)||string.IsNullOrWhiteSpace(_gemini.ApiKey))) throw new InvalidOperationException("Gemini fallback model/API key is not configured.");

        PdfExtractionResult native;
        try { native=await new PdfKnowledgeExtractor().ExtractAsync(path,cancellationToken); }
        catch { return null; }
        var hash=KnowledgeProcessing.Sha256(path);
        var cacheDir=FallbackCacheIdentity.Directory(_options.CachePath,hash,_options.Provider,_options.Model,_options.PipelineVersion);
        Directory.CreateDirectory(cacheDir);
        var recovered=new Dictionary<int,string>();
        var poorPages=native.Pages.Where(IsPoorPage).Select(x=>x.PageNumber).ToList();
        foreach(var page in poorPages.ToList())
        {
            var cache=Path.Combine(cacheDir,$"page-{page:D4}.txt");
            if(File.Exists(cache) && !string.IsNullOrWhiteSpace(await File.ReadAllTextAsync(cache,cancellationToken)))
            { recovered[page]=await File.ReadAllTextAsync(cache,cancellationToken); poorPages.Remove(page); }
        }
        var pdf=useGemini?Convert.ToBase64String(await File.ReadAllBytesAsync(path,cancellationToken)):string.Empty;
        var localUsed=false;
        foreach(var group in poorPages.Chunk(Math.Max(1,_options.PagesPerRequest)))
        {
            IReadOnlyList<VisionPage> extracted;
            if(useGemini)
            {
                try { extracted=await ExtractPagesAsync(pdf,group,cancellationToken); }
                catch when(useLocal) { extracted=await ExtractPagesWithLocalOcrAsync(path,group,cancellationToken); localUsed=true; }
            }
            else { extracted=await ExtractPagesWithLocalOcrAsync(path,group,cancellationToken); localUsed=true; }
            foreach(var item in extracted)
            {
                if(!group.Contains(item.PageNumber) || IsUnusableVisionText(item.Text)) continue;
                recovered[item.PageNumber]=item.Text.Trim();
                await File.WriteAllTextAsync(Path.Combine(cacheDir,$"page-{item.PageNumber:D4}.txt"),item.Text.Trim(),cancellationToken);
            }
        }
        var pages=native.Pages.Select(p=>recovered.TryGetValue(p.PageNumber,out var text)?new PdfPageText(p.PageNumber,text):p).ToList();
        var provider=localUsed?(useGemini?"GeminiThenLocalOcr":"LocalOcr"):"GeminiDocumentVision";
        var method=recovered.Count==native.Pages.Count ? $"{provider}:{_options.PipelineVersion}" : $"Hybrid:{provider}:{_options.PipelineVersion}";
        return new PdfExtractionResult(pages,method);
    }

    private static async Task<IReadOnlyList<VisionPage>> ExtractPagesWithLocalOcrAsync(string pdfPath,int[] pages,CancellationToken ct)
    {
        var poppler=FindExecutable("pdftoppm.exe",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","WinGet","Packages"));
        var installedTesseract=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Tesseract-OCR","tesseract.exe");
        var tesseract=File.Exists(installedTesseract)?installedTesseract:FindExecutable("tesseract.exe",Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var output=new List<VisionPage>();
        var temp=Path.Combine(Path.GetTempPath(),"ledsup-kb-ocr",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            foreach(var page in pages)
            {
                var prefix=Path.Combine(temp,$"page-{page}");
                await RunAsync(poppler,["-f",page.ToString(),"-l",page.ToString(),"-singlefile","-r","180","-png",pdfPath,prefix],ct);
                var text=await RunAsync(tesseract,[prefix+".png","stdout","-l","eng","--psm","6","-c","preserve_interword_spaces=1"],ct);
                output.Add(new(page,text));
            }
        }
        finally { try { Directory.Delete(temp,true); } catch { } }
        return output;
    }
    private static string FindExecutable(string name,string root)
    {
        var command=Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator).Select(x=>Path.Combine(x,name)).FirstOrDefault(File.Exists);
        if(command is not null)return command;
        return Directory.EnumerateFiles(root,name,SearchOption.AllDirectories).FirstOrDefault()??throw new FileNotFoundException($"Required OCR executable {name} was not found.");
    }
    private static async Task<string> RunAsync(string executable,IEnumerable<string> args,CancellationToken ct)
    {
        var psi=new ProcessStartInfo(executable){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true}; foreach(var arg in args)psi.ArgumentList.Add(arg);
        using var process=Process.Start(psi)??throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        var stdout=process.StandardOutput.ReadToEndAsync(ct); var stderr=process.StandardError.ReadToEndAsync(ct); await process.WaitForExitAsync(ct);
        if(process.ExitCode!=0)throw new InvalidOperationException($"{Path.GetFileName(executable)} failed: {(await stderr).Trim()}"); return await stdout;
    }

    private async Task<IReadOnlyList<VisionPage>> ExtractPagesAsync(string pdf,int[] pages,CancellationToken ct)
    {
        var prompt=$"Transcribe ONLY the visible content on PDF page(s) {string.Join(", ",pages)}. Do not summarize, explain, infer, correct, or invent. Preserve headings, model/version names, HDMI/DVI/DP/SDI/USB/Genlock/HDR, counts, dimensions, units, resolutions, refresh rates and every technical number exactly. Preserve tables as Markdown rows so labels remain associated with values. If text is unreadable write [UNREADABLE]. Return strict JSON: {{\"pages\":[{{\"pageNumber\":1,\"text\":\"...\"}}]}}.";
        var body=new { contents=new[]{new{role="user",parts=new object[]{new{text=prompt},new{inline_data=new{mime_type="application/pdf",data=pdf}}}}}, generationConfig=new{temperature=0,maxOutputTokens=16384,responseMimeType="application/json"} };
        for(var attempt=0;;attempt++)
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,$"v1beta/models/{Uri.EscapeDataString(_options.Model)}:generateContent") { Content=JsonContent.Create(body) };
            request.Headers.Add("x-goog-api-key",_gemini.ApiKey);
            using var response=await _http.SendAsync(request,ct);
            if(response.IsSuccessStatusCode)
            {
                using var json=JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
                var text=json.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
                using var payload=JsonDocument.Parse(text);
                return payload.RootElement.GetProperty("pages").EnumerateArray().Select(x=>new VisionPage(x.GetProperty("pageNumber").GetInt32(),x.GetProperty("text").GetString()??"")).ToList();
            }
            var transient=response.StatusCode==HttpStatusCode.TooManyRequests || (int)response.StatusCode>=500 || response.StatusCode==HttpStatusCode.RequestTimeout;
            if(!transient || attempt>=_options.RetryCount) throw new InvalidOperationException($"Gemini fallback failed with HTTP {(int)response.StatusCode} after {attempt+1} attempt(s).");
            var delay=response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Min(30,Math.Pow(2,attempt)));
            await Task.Delay(delay,ct);
        }
    }
    internal static bool IsPoorPage(PdfPageText p) => p.Text.Length<120 || p.Text.Count(c=>char.IsLetterOrDigit(c)||char.IsWhiteSpace(c)) < p.Text.Length*.65;
    internal static bool IsUnusableVisionText(string text) => string.IsNullOrWhiteSpace(text) || text.Length<40 || text.Count(c=>char.IsLetterOrDigit(c)||char.IsWhiteSpace(c)) < text.Length*.6 || text.Contains("[UNREADABLE]",StringComparison.OrdinalIgnoreCase)&&text.Length<100;
    private static string SafeSegment(string value)=>string.Concat(value.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c));
    private sealed record VisionPage(int PageNumber,string Text);
}

public static class FallbackCacheIdentity
{
    public static string Directory(string root,string sha256,string provider,string model,string pipelineVersion)
    {
        static string Safe(string value)=>string.Concat(value.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c));
        return Path.Combine(root,sha256,Safe($"{provider}-{model}-{pipelineVersion}"));
    }
    public static string Page(string root,string sha256,string provider,string model,string pipelineVersion,int pageNumber)=>
        Path.Combine(Directory(root,sha256,provider,model,pipelineVersion),$"page-{pageNumber:D4}.txt");
}
