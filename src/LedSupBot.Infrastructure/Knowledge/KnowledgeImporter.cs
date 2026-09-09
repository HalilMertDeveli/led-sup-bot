using LedSupBot.Application.Knowledge;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector;

namespace LedSupBot.Infrastructure.Knowledge;

public sealed record KnowledgeImportItem(string File, string Hash, ParsedKnowledgeMetadata Metadata, int Pages, ExtractionQuality Quality, string Action, KnowledgeDocumentStatus Status, int Chunks, string? Error=null);

public sealed class KnowledgeImporter(ApplicationDbContext db, IPdfKnowledgeExtractor native, IDocumentFallbackExtractor fallback, IEmbeddingService embeddings, IOptions<KnowledgeOptions> options, IOptions<EmbeddingOptions> embeddingOptions)
{
    public async Task<IReadOnlyList<KnowledgeImportItem>> RunAsync(bool dryRun, CancellationToken cancellationToken)
    {
        var root=Path.GetFullPath(options.Value.RootPath);
        if(!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var files=Directory.EnumerateFiles(root,"*.pdf",SearchOption.AllDirectories).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToList();
        var results=new List<KnowledgeImportItem>();
        foreach(var discovered in files)
        {
            var path=KnowledgeProcessing.ResolveInsideRoot(root,discovered); var relative=Path.GetRelativePath(root,path); var hash=KnowledgeProcessing.Sha256(path); var meta=KnowledgeProcessing.ParseMetadata(relative);
            var existing=dryRun ? null : await db.KnowledgeDocuments.AsNoTracking().FirstOrDefaultAsync(x=>x.Sha256==hash,cancellationToken);
            var pipelineVersion=options.Value.FallbackExtraction.PipelineVersion;
            var fallbackCurrent=existing is not null && (!existing.ExtractionMethod.StartsWith("Hybrid",StringComparison.OrdinalIgnoreCase) && !existing.ExtractionMethod.StartsWith("GeminiDocumentVision",StringComparison.OrdinalIgnoreCase) && !existing.ExtractionMethod.StartsWith("LocalOcr",StringComparison.OrdinalIgnoreCase) || existing.ExtractionVersion==pipelineVersion);
            if(existing?.Status==KnowledgeDocumentStatus.Ready && fallbackCurrent) { results.Add(new(relative,hash,meta,existing.PageCount,existing.ExtractionQuality,"skip-unchanged",existing.Status,await db.KnowledgeChunks.CountAsync(x=>x.DocumentId==existing.Id,cancellationToken))); continue; }
            PdfExtractionResult extraction; ExtractionQuality quality;
            try { extraction=await native.ExtractAsync(path,cancellationToken); quality=KnowledgeProcessing.Assess(extraction.Pages,meta.Model); }
            catch(Exception ex) { results.Add(await RecordFailure(dryRun,relative,hash,meta,KnowledgeDocumentStatus.Failed,ex.Message,cancellationToken)); continue; }
            if(quality==ExtractionQuality.Poor)
            {
                PdfExtractionResult? recovered;
                try { recovered=await fallback.ExtractAsync(path,cancellationToken); }
                catch(Exception ex) { results.Add(await RecordFailure(dryRun,relative,hash,meta,KnowledgeDocumentStatus.NeedsFallback,$"Fallback failed safely: {ex.Message}",cancellationToken,extraction.Pages.Count)); continue; }
                if(recovered is null || KnowledgeProcessing.Assess(recovered.Pages,meta.Model)==ExtractionQuality.Poor)
                { results.Add(await RecordFailure(dryRun,relative,hash,meta,KnowledgeDocumentStatus.NeedsFallback,"Native extraction poor; configured OCR/document-vision fallback unavailable or unusable.",cancellationToken,extraction.Pages.Count)); continue; }
                extraction=recovered; quality=KnowledgeProcessing.Assess(extraction.Pages,meta.Model);
            }
            var chunks=KnowledgeProcessing.Chunk(extraction.Pages,options.Value.ChunkTargetCharacters,options.Value.ChunkOverlapCharacters);
            if(chunks.Count==0) { results.Add(await RecordFailure(dryRun,relative,hash,meta,KnowledgeDocumentStatus.Rejected,"No usable chunks produced.",cancellationToken,extraction.Pages.Count)); continue; }
            if(dryRun) { results.Add(new(relative,hash,meta,extraction.Pages.Count,quality,"would-import",KnowledgeDocumentStatus.Pending,chunks.Count)); continue; }
            try
            {
                var vectors=new List<float[]>(); var batch=Math.Max(1,embeddingOptions.Value.BatchSize);
                for(var i=0;i<chunks.Count;i+=batch) vectors.AddRange(await embeddings.EmbedAsync(chunks.Skip(i).Take(batch).Select(x=>x.Content).ToList(),cancellationToken));
                if(vectors.Any(x=>x.Length!=embeddings.Dimensions)) throw new InvalidOperationException("Embedding dimensions do not match configuration.");
                await using var tx=await db.Database.BeginTransactionAsync(cancellationToken);
                var stale=await db.KnowledgeDocuments.Include(x=>x.Chunks).Where(x=>x.RelativePath==relative || x.Sha256==hash).ToListAsync(cancellationToken);
                db.KnowledgeDocuments.RemoveRange(stale);
                var doc=new KnowledgeDocument { Manufacturer=meta.Manufacturer,ProductType=meta.ProductType,ProductFamily=meta.ProductFamily,Model=meta.Model,DocumentType=meta.DocumentType,DocumentVersion=meta.Version,Language=meta.Language,OriginalFileName=Path.GetFileName(path),RelativePath=relative,Sha256=hash,PageCount=extraction.Pages.Count,ExtractionMethod=extraction.Method,ExtractionVersion=extraction.Method.Contains("vision",StringComparison.OrdinalIgnoreCase)||extraction.Method.Contains("Ocr",StringComparison.OrdinalIgnoreCase)||extraction.Method.StartsWith("Hybrid",StringComparison.OrdinalIgnoreCase)?pipelineVersion:"native-v1",ExtractionQuality=quality,Status=KnowledgeDocumentStatus.Ready,ImportedAtUtc=DateTimeOffset.UtcNow };
                for(var i=0;i<chunks.Count;i++) doc.Chunks.Add(new KnowledgeChunk { ChunkIndex=i,PageStart=chunks[i].Start,PageEnd=chunks[i].End,SectionTitle=chunks[i].Section,Content=chunks[i].Content,Embedding=new Vector(vectors[i]) });
                db.KnowledgeDocuments.Add(doc); await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
                results.Add(new(relative,hash,meta,extraction.Pages.Count,quality,"imported",doc.Status,chunks.Count));
            }
            catch(Exception ex) { db.ChangeTracker.Clear(); results.Add(await RecordFailure(false,relative,hash,meta,KnowledgeDocumentStatus.Failed,ex.GetBaseException().Message,cancellationToken,extraction.Pages.Count)); }
        }
        return results;
    }
    private async Task<KnowledgeImportItem> RecordFailure(bool dryRun,string relative,string hash,ParsedKnowledgeMetadata meta,KnowledgeDocumentStatus status,string error,CancellationToken ct,int pages=0)
    {
        if(!dryRun)
        {
            var doc=await db.KnowledgeDocuments.FirstOrDefaultAsync(x=>x.Sha256==hash,ct);
            if(doc?.Status==KnowledgeDocumentStatus.Ready)
                return new(relative,hash,meta,doc.PageCount,doc.ExtractionQuality,"reprocess-failed-preserved",KnowledgeDocumentStatus.Ready,await db.KnowledgeChunks.CountAsync(x=>x.DocumentId==doc.Id,ct),error);
            if(doc is null) { doc=new KnowledgeDocument { OriginalFileName=Path.GetFileName(relative),RelativePath=relative,Sha256=hash,Manufacturer=meta.Manufacturer,ProductType=meta.ProductType,ProductFamily=meta.ProductFamily,Model=meta.Model,DocumentType=meta.DocumentType,DocumentVersion=meta.Version,Language=meta.Language }; db.Add(doc); }
            doc.PageCount=pages; doc.Status=status; doc.ExtractionQuality=ExtractionQuality.Poor; await db.SaveChangesAsync(ct);
        }
        return new(relative,hash,meta,pages,ExtractionQuality.Poor,dryRun?"would-require-fallback":"failed",status,0,error);
    }
}
