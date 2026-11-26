// Mock wholesale price API. Fails a configurable share of calls (FAIL_RATE) to exercise Polly in the API.
var failRate = double.TryParse(Environment.GetEnvironmentVariable("FAIL_RATE"), System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0.5;
var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddJsonConsole();
var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapGet("/price/{region}", (string region, ILogger<Program> log) =>
{
    if (Random.Shared.NextDouble() < failRate)
    {
        log.LogWarning("Injecting failure for region {Region}", region);
        return Results.StatusCode(Random.Shared.Next(2) == 0 ? 503 : 500);
    }
    var price = Math.Round(0.18m + (decimal)Random.Shared.NextDouble() * 0.04m, 4);
    return Results.Ok(new { region, pricePerKwh = price });
});

app.Run();

public partial class Program;
