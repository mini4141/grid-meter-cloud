using Amazon.SQS;
using FluentValidation;
using Finbuckle.MultiTenant.Abstractions;
using GridMeter.Infrastructure.Persistence;

namespace GridMeter.Api.Infrastructure;

/// <summary>Maps validation and not-found exceptions from the MediatR pipeline to HTTP responses.</summary>
public sealed class ErrorMappingMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext ctx)
    {
        try { await next(ctx); }
        catch (ValidationException ex)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(new
            {
                title = "Validation failed",
                errors = ex.Errors.Select(e => new { field = e.PropertyName, message = e.ErrorMessage })
            });
        }
        catch (HttpRequestException)
        {
            // e.g. the wholesale price API stayed down after all Polly retries
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await ctx.Response.WriteAsJsonAsync(new { title = "A dependency is unavailable, try again" });
        }
        catch (KeyNotFoundException ex)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            await ctx.Response.WriteAsJsonAsync(new { title = ex.Message });
        }
    }
}

/// <summary>Every /api call (except login and admin info) needs a tenant, and the JWT tenant claim must match it.</summary>
public sealed class TenantGuardMiddleware(RequestDelegate next)
{
    private static readonly string[] Open = ["/api/auth", "/api/admin/info"];

    public async Task Invoke(HttpContext ctx, IMultiTenantContextAccessor accessor)
    {
        var path = ctx.Request.Path.Value ?? "";
        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) && !Open.Any(o => path.StartsWith(o, StringComparison.OrdinalIgnoreCase)))
        {
            var tenant = accessor.MultiTenantContext?.TenantInfo?.Id;
            if (tenant is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsJsonAsync(new { title = "Unknown or missing X-Tenant header" });
                return;
            }
            var claim = ctx.User.FindFirst("tenant")?.Value;
            if (ctx.User.Identity?.IsAuthenticated == true && claim != tenant)
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new { title = "Token does not belong to this tenant" });
                return;
            }
        }
        await next(ctx);
    }
}

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/readyz", async (AppDbContext db, IAmazonSQS sqs, CancellationToken ct) =>
        {
            var dbOk = await db.Database.CanConnectAsync(ct);
            var sqsOk = true;
            try { await sqs.ListQueuesAsync(new Amazon.SQS.Model.ListQueuesRequest { MaxResults = 1 }, ct); }
            catch { sqsOk = false; }
            var body = new { database = dbOk, queue = sqsOk };
            return dbOk && sqsOk ? Results.Ok(body) : Results.Json(body, statusCode: 503);
        });
    }
}
