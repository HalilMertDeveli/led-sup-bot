using LedSupBot.Application.Knowledge;
using LedSupBot.Domain.Enums;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace LedSupBot.Infrastructure.Knowledge;

public sealed class TechnicalKnowledgeRetriever(ApplicationDbContext db, IEmbeddingService embeddings) : ITechnicalKnowledgeRetriever
{
    public async Task<IReadOnlyList<TechnicalKnowledgeResult>> SearchAsync(TechnicalKnowledgeQuery query, CancellationToken cancellationToken)
    {
        var vector = new Vector((await embeddings.EmbedAsync([query.Text], cancellationToken))[0]);
        var docs=db.KnowledgeDocuments.Where(x=>x.Status==KnowledgeDocumentStatus.Ready);
        if(query.Model is not null) { var normalized=KnowledgeProcessing.NormalizeModel(query.Model); docs=docs.Where(x=>x.Model==normalized); }
        if(query.Manufacturer is not null) docs=docs.Where(x=>x.Manufacturer==query.Manufacturer);
        if(query.ProductType is not null) docs=docs.Where(x=>x.ProductType==query.ProductType);
        if(query.ProductFamily is not null) docs=docs.Where(x=>x.ProductFamily==query.ProductFamily);
        var rows=await db.KnowledgeChunks.Join(docs,c=>c.DocumentId,d=>d.Id,(c,d)=>new{c,d,Distance=c.Embedding.CosineDistance(vector)})
            .OrderBy(x=>x.Distance).Take(Math.Clamp(query.TopK,1,20)).ToListAsync(cancellationToken);
        return rows.Select(x=>new TechnicalKnowledgeResult(x.c.Content,1d-x.Distance,x.d.OriginalFileName,x.d.Model,x.c.PageStart,x.c.PageEnd,x.c.SectionTitle)).ToList();
    }
}
