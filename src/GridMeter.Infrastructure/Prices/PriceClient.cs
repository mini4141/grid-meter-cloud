using System.Net.Http.Json;
using GridMeter.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace GridMeter.Infrastructure.Prices;

public sealed class PriceClient(HttpClient http) : IPriceClient
{
    private sealed record PriceResponse(decimal PricePerKwh);

    public async Task<decimal> GetPricePerKwhAsync(string region, CancellationToken ct)
    {
        var r = await http.GetFromJsonAsync<PriceResponse>($"price/{Uri.EscapeDataString(region)}", ct);
        return r!.PricePerKwh;
    }
}

public static class PriceClientRegistration
{
    public static IServiceCollection AddPriceClient(this IServiceCollection services, string baseUrl)
    {
        services.AddHttpClient<IPriceClient, PriceClient>(c => c.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"))
            .AddResilienceHandler("wholesale-price", (builder, ctx) =>
            {
                var log = ctx.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PriceResilience");
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromMilliseconds(200),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    OnRetry = args =>
                    {
                        log.LogWarning("Price API retry {Attempt} after {Outcome}", args.AttemptNumber + 1,
                            args.Outcome.Result?.StatusCode.ToString() ?? args.Outcome.Exception?.GetType().Name);
                        return default;
                    }
                });
                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.9,
                    MinimumThroughput = 10,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromSeconds(5),
                    OnOpened = _ => { log.LogError("Price API circuit breaker OPEN"); return default; },
                    OnClosed = _ => { log.LogInformation("Price API circuit breaker closed"); return default; }
                });
            });
        return services;
    }
}
