using DotNet.Testcontainers.Builders;
using LedSupBot.Application.Knowledge;
using LedSupBot.Domain.Enums;
using LedSupBot.Infrastructure.Knowledge;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector.EntityFrameworkCore;
using Npgsql;
using Pgvector;
using Testcontainers.PostgreSql;

namespace LedSupBot.IntegrationTests;

[CollectionDefinition("knowledge-importer",DisableParallelization=true)]
public sealed class KnowledgeImporterCollection:ICollectionFixture<KnowledgeImporterFixture>;

[Collection("knowledge-importer")]
public sealed class KnowledgeImporterFailurePathTests(KnowledgeImporterFixture fixture)
{
    [Fact] public async Task F1_transient_429_eventually_persists_ready()
    {
        await fixture.ResetAsync(); using var corpus=Corpus.Create("A1 Specification V1.0.pdf");
        var embedding=new RetryingEmbedding(1); var rows=await fixture.Import(corpus,embedding);
        Assert.Equal(2,embedding.Attempts); var row=Assert.Single(rows); Assert.True(row.Status==KnowledgeDocumentStatus.Ready,row.Error); Assert.Equal(1,await fixture.ReadyCount());
    }

    [Fact] public async Task F2_and_F5_permanent_4xx_isolated_and_next_document_continues()
    {
        await fixture.ResetAsync(); using var corpus=Corpus.Create("A1 Specification V1.0.pdf","A2 Specification V1.0.pdf");
        var rows=await fixture.Import(corpus,new SelectiveEmbedding("A1"));
        Assert.Equal(KnowledgeDocumentStatus.Failed,rows.Single(x=>x.Metadata.Model=="A1").Status);
        Assert.Equal(KnowledgeDocumentStatus.Ready,rows.Single(x=>x.Metadata.Model=="A2").Status);
        Assert.Equal(1,await fixture.ReadyCount());
    }

    [Fact] public async Task F3_and_F6_interrupted_fallback_resumes_without_duplicates()
    {
        await fixture.ResetAsync(); using var corpus=Corpus.Create("A1 Specification V1.0.pdf"); var fallback=new ResumableFallback();
        var first=await fixture.Import(corpus,new RetryingEmbedding(0),fallback,new PoorExtractor());
        Assert.Equal(KnowledgeDocumentStatus.NeedsFallback,Assert.Single(first).Status); Assert.Equal(1,fallback.CompletedPages);
        var second=await fixture.Import(corpus,new RetryingEmbedding(0),fallback,new PoorExtractor());
        Assert.Equal(KnowledgeDocumentStatus.Ready,Assert.Single(second).Status); Assert.Equal(2,fallback.CompletedPages);
        Assert.Equal(2,await fixture.ChunkCount());
        var third=await fixture.Import(corpus,new RetryingEmbedding(0),fallback,new PoorExtractor());
        Assert.Equal("skip-unchanged",Assert.Single(third).Action); Assert.Equal(2,await fixture.ChunkCount());
    }

    [Fact] public void F4_cache_identity_changes_with_provider_model_version_and_page()
    {
        var a=FallbackCacheIdentity.Page("root","hash","Gemini","m1","v1",1);
        Assert.Equal(a,FallbackCacheIdentity.Page("root","hash","Gemini","m1","v1",1));
        Assert.NotEqual(a,FallbackCacheIdentity.Page("root","hash","LocalOcr","m1","v1",1));
        Assert.NotEqual(a,FallbackCacheIdentity.Page("root","hash","Gemini","m2","v1",1));
        Assert.NotEqual(a,FallbackCacheIdentity.Page("root","hash","Gemini","m1","v2",1));
        Assert.NotEqual(a,FallbackCacheIdentity.Page("root","hash","Gemini","m1","v1",2));
    }

    [Fact] public async Task F7_complete_rerun_skips_embedding_and_preserves_traceability()
    {
        await fixture.ResetAsync(); using var corpus=Corpus.Create("A1 Specification V1.0.pdf"); var embedding=new RetryingEmbedding(0);
        await fixture.Import(corpus,embedding); var attempts=embedding.Attempts; var rows=await fixture.Import(corpus,embedding);
        Assert.Equal("skip-unchanged",Assert.Single(rows).Action); Assert.Equal(attempts,embedding.Attempts); Assert.Equal(1,await fixture.ChunkCount());
        await using var db=fixture.Context(); var chunk=await db.KnowledgeChunks.Include(x=>x.Document).SingleAsync(); Assert.Equal(1,chunk.PageStart);Assert.Equal("A1",chunk.Document.Model);
    }

    [Fact] public async Task F8_failed_versioned_replacement_preserves_old_ready_transactionally()
    {
        await fixture.ResetAsync(); using var corpus=Corpus.Create("A1 Specification V1.0.pdf");
        await fixture.Import(corpus,new RetryingEmbedding(0),new GoodFallback(),new PoorExtractor(),"v1");
        var before=await fixture.ChunkCount(); var rows=await fixture.Import(corpus,new WrongDimensionEmbedding(),new GoodFallback(),new PoorExtractor(),"v2");
        Assert.Equal("reprocess-failed-preserved",Assert.Single(rows).Action); Assert.Equal(KnowledgeDocumentStatus.Ready,rows[0].Status);
        Assert.Equal(before,await fixture.ChunkCount()); Assert.Equal(1,await fixture.ReadyCount());
    }
}

public sealed class KnowledgeImporterFixture:IAsyncLifetime
{
    private readonly PostgreSqlContainer _db=new PostgreSqlBuilder().WithImage("pgvector/pgvector:pg16").WithDatabase("knowledge_tests").WithUsername("postgres").WithPassword("postgres").Build();
    private NpgsqlDataSource _dataSource=null!;
    public async Task InitializeAsync(){await _db.StartAsync();await using(var connection=new NpgsqlConnection(_db.GetConnectionString())){await connection.OpenAsync();await using var command=new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector",connection);await command.ExecuteNonQueryAsync();}var builder=new NpgsqlDataSourceBuilder(_db.GetConnectionString());builder.UseVector();_dataSource=builder.Build();await using var db=Context();await db.Database.MigrateAsync();}
    public async Task DisposeAsync(){await _dataSource.DisposeAsync();await _db.DisposeAsync();}
    public ApplicationDbContext Context()=>new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_dataSource,x=>x.UseVector()).Options);
    public async Task ResetAsync(){await using var db=Context();await db.Database.ExecuteSqlRawAsync("TRUNCATE knowledge_chunks, knowledge_documents CASCADE");}
    public async Task<int> ReadyCount(){await using var db=Context();return await db.KnowledgeDocuments.CountAsync(x=>x.Status==KnowledgeDocumentStatus.Ready);}
    public async Task<int> ChunkCount(){await using var db=Context();return await db.KnowledgeChunks.CountAsync();}
    public async Task<IReadOnlyList<KnowledgeImportItem>> Import(Corpus corpus,IEmbeddingService embedding,IDocumentFallbackExtractor? fallback=null,IPdfKnowledgeExtractor? native=null,string version="v1")
    { await using var db=Context();var options=Options.Create(new KnowledgeOptions{RootPath=corpus.Root,FallbackExtraction=new(){PipelineVersion=version}});return await new KnowledgeImporter(db,native??new GoodExtractor(),fallback??new GoodFallback(),embedding,options,Options.Create(new EmbeddingOptions{BatchSize=10})).RunAsync(false,CancellationToken.None); }
}

public sealed class Corpus:IDisposable
{
    public string Root{get;} private Corpus(string root){Root=root;}
    public static Corpus Create(params string[] files){var root=Path.Combine(Path.GetTempPath(),"ledsup-import-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);foreach(var f in files)File.WriteAllText(Path.Combine(root,f),f);return new(root);}
    public void Dispose(){try{Directory.Delete(Root,true);}catch{}}
}
file sealed class GoodExtractor:IPdfKnowledgeExtractor { public Task<PdfExtractionResult> ExtractAsync(string path,CancellationToken ct){var model=KnowledgeProcessing.ParseMetadata(Path.GetFileName(path)).Model;return Task.FromResult(new PdfExtractionResult([new(1,$"{model} Specification HDMI Inputs "+new string('a',500))],"native"));} }
file sealed class PoorExtractor:IPdfKnowledgeExtractor { public Task<PdfExtractionResult> ExtractAsync(string path,CancellationToken ct)=>Task.FromResult(new PdfExtractionResult([new(1,"@@"),new(2,"@@")],"native")); }
file sealed class GoodFallback:IDocumentFallbackExtractor { public Task<PdfExtractionResult?> ExtractAsync(string path,CancellationToken ct){var m=KnowledgeProcessing.ParseMetadata(Path.GetFileName(path)).Model;return Task.FromResult<PdfExtractionResult?>(new([new(1,$"{m} Specification HDMI "+new string('a',500)),new(2,"Outputs Ethernet "+new string('b',500))],"Hybrid:v"));} }
file sealed class ResumableFallback:IDocumentFallbackExtractor { public int CompletedPages{get;private set;} public Task<PdfExtractionResult?> ExtractAsync(string path,CancellationToken ct){if(CompletedPages==0){CompletedPages=1;throw new InvalidOperationException("simulated interruption after cached page");}CompletedPages=2;return new GoodFallback().ExtractAsync(path,ct);} }
file sealed class RetryingEmbedding(int transientFailures):IEmbeddingService { public string Provider=>"fake";public string Model=>"fake";public int Dimensions=>768;public int Attempts{get;private set;} public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,CancellationToken ct){for(var i=0;i<transientFailures;i++){Attempts++;}Attempts++;return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_=>Vector()).ToList());} private static float[] Vector(){var v=new float[768];v[0]=1;return v;} }
file sealed class SelectiveEmbedding(string failingModel):IEmbeddingService { public string Provider=>"fake";public string Model=>"fake";public int Dimensions=>768;public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,CancellationToken ct){if(texts.Any(x=>x.Contains(failingModel)))throw new InvalidOperationException("HTTP 400");return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_=>{var v=new float[768];v[0]=1;return v;}).ToList());} }
file sealed class WrongDimensionEmbedding:IEmbeddingService { public string Provider=>"fake";public string Model=>"fake";public int Dimensions=>768;public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,CancellationToken ct)=>Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_=>new float[3]).ToList()); }
