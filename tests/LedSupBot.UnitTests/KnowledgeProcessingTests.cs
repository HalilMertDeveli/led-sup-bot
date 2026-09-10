using LedSupBot.Application.Knowledge;
using LedSupBot.Domain.Enums;
using LedSupBot.Infrastructure.Knowledge;

namespace LedSupBot.UnitTests;

public sealed class KnowledgeProcessingTests
{
    [Theory]
    [InlineData("X4e-Specification V1.2.pdf","X4e","V1.2","Processor")]
    [InlineData("S20PRO Specification V2.0.pdf","S20PRO","V2.0","Splitter")]
    [InlineData("Z6 PRO G2 Specification V1.0.pdf","Z6 Pro G2","V1.0","Processor")]
    public void Metadata_is_deterministic(string file,string model,string version,string type)
    { var x=KnowledgeProcessing.ParseMetadata(file); Assert.Equal(model,x.Model); Assert.Equal(version,x.Version); Assert.Equal(type,x.ProductType); }

    [Fact] public void Similar_models_remain_distinct()
    { Assert.Equal(6,new[]{"X20","X20m","Z6","Z6 Pro","Z6 Pro G2","S20F"}.Select(KnowledgeProcessing.NormalizeModel).Distinct().Count()); }

    [Fact] public void Good_native_text_passes_quality_gate()
    { var text="X4e Specification HDMI Input Outputs Loading Capacity "+new string('a',500); Assert.Equal(ExtractionQuality.Good,KnowledgeProcessing.Assess([new(1,text)],"X4e")); }

    [Fact] public void Poor_native_text_triggers_quality_gate()
    { Assert.Equal(ExtractionQuality.Poor,KnowledgeProcessing.Assess([new(1,"@@")],"X4e")); }

    [Fact] public void Chunk_retains_page_traceability()
    { var x=KnowledgeProcessing.Chunk([new(7,"Outputs "+new string('x',300))]); Assert.All(x,c=>{Assert.Equal(7,c.Start);Assert.Equal(7,c.End);}); }

    [Fact] public void Root_guard_rejects_outside_paths()
    { var root=Path.Combine(Path.GetTempPath(),"kb-root"); Assert.Throws<InvalidOperationException>(()=>KnowledgeProcessing.ResolveInsideRoot(root,Path.Combine(Path.GetTempPath(),"secret.pdf"))); }

    [Fact] public void Hash_uses_content_not_filename()
    { var p=Path.GetTempFileName(); try { File.WriteAllText(p,"one");var a=KnowledgeProcessing.Sha256(p);File.WriteAllText(p,"two");Assert.NotEqual(a,KnowledgeProcessing.Sha256(p)); } finally { File.Delete(p); } }
}
