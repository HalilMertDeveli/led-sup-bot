using System.Net;
using System.Text;
using LedSupBot.Infrastructure.Configuration;
using LedSupBot.Infrastructure.Knowledge;
using Microsoft.Extensions.Options;

namespace LedSupBot.UnitTests;

public sealed class GeminiEmbeddingFailureTests
{
    [Fact]
    public async Task Rate_limit_is_bounded_and_eventually_succeeds()
    {
        var handler=new SequenceHandler(HttpStatusCode.TooManyRequests,HttpStatusCode.OK);
        var service=Create(handler);
        var result=await service.EmbedAsync(["technical"],CancellationToken.None);
        Assert.Single(result); Assert.Equal(2,handler.Calls);
    }

    [Fact]
    public async Task Invalid_request_fails_fast_without_retry()
    {
        var handler=new SequenceHandler(HttpStatusCode.BadRequest);
        var service=Create(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.EmbedAsync(["technical"],CancellationToken.None));
        Assert.Equal(1,handler.Calls);
    }

    private static GeminiEmbeddingService Create(SequenceHandler handler)=>new(new HttpClient(handler){BaseAddress=new Uri("https://example.invalid/")},Options.Create(new GeminiOptions{ApiKey="test-only"}),Options.Create(new EmbeddingOptions{Model="test",Dimensions=3}));
    private sealed class SequenceHandler(params HttpStatusCode[] statuses):HttpMessageHandler
    {
        public int Calls {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            var status=statuses[Math.Min(Calls++,statuses.Length-1)];
            var response=new HttpResponseMessage(status);
            if(status==HttpStatusCode.OK)response.Content=new StringContent("{\"embedding\":{\"values\":[0.1,0.2,0.3]}}",Encoding.UTF8,"application/json");
            return Task.FromResult(response);
        }
    }
}
