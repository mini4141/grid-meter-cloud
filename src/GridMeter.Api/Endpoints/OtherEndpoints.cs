using System.Reflection;
using FastEndpoints;
using FastEndpoints.Security;
using Finbuckle.MultiTenant.Abstractions;
using GridMeter.Api.Configuration;
using GridMeter.Application.Abstractions;
using GridMeter.Application.DemandResponse;
using GridMeter.Application.Readings;
using MediatR;
using Microsoft.FeatureManagement;

namespace GridMeter.Api.Endpoints;

// ---- auth (dev JWT per tenant user) ----
public sealed class TokenRequest
{
    public string Tenant { get; set; } = "";
    public string User { get; set; } = "";
}

public sealed record TokenResponse(string Token, string Tenant, string User);

public sealed class TokenEndpoint(IMultiTenantStore<TenantInfo> tenants, IConfiguration config) : Endpoint<TokenRequest, TokenResponse>
{
    public override void Configure()
    {
        Post("/auth/token");
        AllowAnonymous();
    }

    public override async Task HandleAsync(TokenRequest req, CancellationToken ct)
    {
        var tenant = await tenants.GetByIdentifierAsync(req.Tenant);
        if (tenant is null || string.IsNullOrWhiteSpace(req.User))
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }
        var token = JwtBearer.CreateToken(o =>
        {
            o.SigningKey = config["Jwt:Key"]!;
            o.ExpireAt = DateTime.UtcNow.AddHours(8);
            o.User.Claims.Add(("tenant", tenant.Id!));
            o.User.Claims.Add(("name", req.User));
        });
        await Send.OkAsync(new TokenResponse(token, tenant.Id!, req.User), ct);
    }
}

// ---- uploads ----
public sealed class UploadRequest
{
    public string FileName { get; set; } = "readings.csv";
}

public sealed class UploadEndpoint(IUploadUrlService uploads, ICurrentTenant tenant) : Endpoint<UploadRequest, UploadTicket>
{
    public override void Configure() => Post("/uploads");

    public override async Task HandleAsync(UploadRequest req, CancellationToken ct) =>
        await Send.OkAsync(uploads.CreateTicket(tenant.Id!, req.FileName), ct);
}

// ---- consumption / estimate ----
public sealed class ConsumptionRequest
{
    public Guid Id { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public sealed class ConsumptionEndpoint(IMediator mediator) : Endpoint<ConsumptionRequest, ConsumptionDto>
{
    public override void Configure() => Get("/sites/{id}/consumption");

    public override async Task HandleAsync(ConsumptionRequest req, CancellationToken ct) =>
        await Send.OkAsync(await mediator.Send(new GetConsumptionQuery(req.Id, req.From, req.To), ct), ct);
}

public sealed class EstimateRequest
{
    public Guid Id { get; set; }
}

public sealed class EstimateEndpoint(IMediator mediator, IFeatureManager features) : Endpoint<EstimateRequest, EstimateDto>
{
    public override void Configure() => Get("/sites/{id}/estimate");

    public override async Task HandleAsync(EstimateRequest req, CancellationToken ct)
    {
        if (!await features.IsEnabledAsync("tariff-estimates"))
        {
            await Send.NotFoundAsync(ct);
            return;
        }
        await Send.OkAsync(await mediator.Send(new GetEstimateQuery(req.Id), ct), ct);
    }
}

// ---- demand response ----
public sealed class DemandResponseRequest
{
    public Guid Id { get; set; }
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public decimal TargetKw { get; set; }
}

public sealed record DemandResponseIssuedDto(Guid EventId);

public sealed class IssueDemandResponseEndpoint(IMediator mediator) : Endpoint<DemandResponseRequest, DemandResponseIssuedDto>
{
    public override void Configure() => Post("/sites/{id}/demand-response");

    public override async Task HandleAsync(DemandResponseRequest req, CancellationToken ct) =>
        await Send.OkAsync(new DemandResponseIssuedDto(
            await mediator.Send(new IssueDemandResponseCommand(req.Id, req.WindowStart, req.WindowEnd, req.TargetKw), ct)), ct);
}

public sealed class ListDemandResponseEndpoint(IMediator mediator) : Endpoint<EstimateRequest, IReadOnlyList<DemandResponseDto>>
{
    public override void Configure() => Get("/sites/{id}/demand-response");

    public override async Task HandleAsync(EstimateRequest req, CancellationToken ct) =>
        await Send.OkAsync(await mediator.Send(new ListDemandResponseQuery(req.Id), ct), ct);
}

// ---- admin ----
public sealed record AdminInfo(string Version, string Environment, bool TariffEstimates, bool CanToggleFlags);

public sealed class AdminInfoEndpoint(IFeatureManager features, IWebHostEnvironment env, IConfiguration config) : EndpointWithoutRequest<AdminInfo>
{
    public override void Configure()
    {
        Get("/admin/info");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var version = Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
        await Send.OkAsync(new AdminInfo(config["BUILD_SHA"] ?? version, env.EnvironmentName,
            await features.IsEnabledAsync("tariff-estimates"), config.GetValue<bool>("Flags:AllowRuntimeToggle")), ct);
    }
}

public sealed class ToggleFlagRequest
{
    public bool Enabled { get; set; }
}

public sealed class ToggleFlagEndpoint(RuntimeOverridesProvider overrides, IConfiguration config) : Endpoint<ToggleFlagRequest>
{
    public override void Configure() => Put("/admin/flags/tariff-estimates");

    public override async Task HandleAsync(ToggleFlagRequest req, CancellationToken ct)
    {
        if (!config.GetValue<bool>("Flags:AllowRuntimeToggle"))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }
        overrides.Override("FeatureManagement:tariff-estimates", req.Enabled ? "true" : "false");
        await Send.NoContentAsync(ct);
    }
}
