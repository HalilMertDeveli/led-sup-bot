using System.Text.Json;
using LedSupBot.Application.Knowledge;
using LedSupBot.Infrastructure;
using LedSupBot.Infrastructure.Configuration;
using LedSupBot.Infrastructure.Knowledge;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder=Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(),"src","LedSupBot.Api","appsettings.json"),optional:true).AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(),"appsettings.Local.json"),optional:true).AddEnvironmentVariables().AddFlatEnvironmentVariableBridge();
builder.Services.AddInfrastructure(builder.Configuration);
using var host=builder.Build(); using var scope=host.Services.CreateScope(); var sp=scope.ServiceProvider;
var command=args.FirstOrDefault()?.ToLowerInvariant() ?? "status";
if(command=="import")
{
    var dry=args.Contains("--dry-run"); var rows=await sp.GetRequiredService<KnowledgeImporter>().RunAsync(dry,CancellationToken.None);
    foreach(var row in rows) Console.WriteLine(JsonSerializer.Serialize(row));
    Console.WriteLine(JsonSerializer.Serialize(new { discovered=rows.Count,pages=rows.Sum(x=>x.Pages),imported=rows.Count(x=>x.Action=="imported"),skipped=rows.Count(x=>x.Action=="skip-unchanged"),ready=rows.Count(x=>x.Status==LedSupBot.Domain.Enums.KnowledgeDocumentStatus.Ready),chunks=rows.Sum(x=>x.Chunks) }));
}
else if(command=="search")
{
    string? Value(string n) { var i=Array.IndexOf(args,n); return i>=0&&i+1<args.Length?args[i+1]:null; }
    var query=Value("--query")??throw new ArgumentException("--query is required"); var model=Value("--model");
    var rows=await sp.GetRequiredService<ITechnicalKnowledgeRetriever>().SearchAsync(new(query,Model:model),CancellationToken.None);
    foreach(var x in rows) Console.WriteLine(JsonSerializer.Serialize(new{x.Score,x.Document,x.Model,x.PageStart,x.PageEnd,x.SectionTitle,Excerpt=x.Content[..Math.Min(240,x.Content.Length)]}));
}
else
{
    var db=sp.GetRequiredService<ApplicationDbContext>(); var docs=await db.KnowledgeDocuments.GroupBy(x=>x.Status).Select(x=>new{x.Key,Count=x.Count()}).ToListAsync();
    Console.WriteLine(JsonSerializer.Serialize(new{documents=docs,chunks=await db.KnowledgeChunks.CountAsync()}));
}
