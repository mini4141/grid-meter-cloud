using Amazon.Runtime;

namespace GridMeter.Infrastructure.Messaging;

/// <summary>
/// moto (the local AWS stand-in) returns an x-amz-crc32 header that AWSSDK v4 rejects for SQS JSON responses.
/// Locally we drop that header; against real AWS this factory is not used.
/// </summary>
public sealed class LocalHttpClientFactory : HttpClientFactory
{
    public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(new StripCrcHandler(new HttpClientHandler()));

    private sealed class StripCrcHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            response.Headers.Remove("x-amz-crc32");
            return response;
        }
    }
}
