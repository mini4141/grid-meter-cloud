using FastEndpoints;
using FastEndpoints.Swagger;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Extensions;
using Finbuckle.MultiTenant.AspNetCore.Extensions;
using Finbuckle.MultiTenant.Abstractions;
using FastEndpoints.Security;
using GridMeter.Api.Configuration;
using GridMeter.Api.Infrastructure;
using GridMeter.Application;
using GridMeter.Infrastructure;
using GridMeter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.FeatureManagement;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

builder.AddAwsConfiguration();

var overrides = new RuntimeOverridesProvider();
((IConfigurationBuilder)builder.Configuration).Add(new RuntimeOverridesSource(overrides));
builder.Services.AddSingleton(overrides);

builder.Services.AddMultiTenant<TenantInfo>()
    .WithHeaderStrategy("X-Tenant")
    .WithHostStrategy("__tenant__.*")
    .WithConfigurationStore();

// The SPA may be served from another origin (S3 website); auth is a bearer token, so open CORS is fine for the showcase.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddFeatureManagement();

var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured");
builder.Services.AddAuthenticationJwtBearer(s => s.SigningKey = jwtKey).AddAuthorization();
builder.Services.AddFastEndpoints();
builder.Services.SwaggerDocument(o =>
{
    o.ShortSchemaNames = true;
    o.DocumentSettings = s =>
    {
        s.Title = "Grid Meter Cloud API";
        s.Version = "v1";
    };
});

var app = builder.Build();

if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    Log.Information("Database migrations applied");
    return;
}

app.UseCors();
app.UseMiddleware<ErrorMappingMiddleware>();
app.UseMultiTenant();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantGuardMiddleware>();
app.UseFastEndpoints(c =>
{
    c.Endpoints.RoutePrefix = "api";
    c.Serializer.Options.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
}).UseSwaggerGen();

app.MapHealthEndpoints();

await app.Services.ProvisionLocalAwsAsync(app.Configuration);
app.Run();

public partial class Program;
