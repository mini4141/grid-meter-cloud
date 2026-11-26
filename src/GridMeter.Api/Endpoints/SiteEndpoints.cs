using FastEndpoints;
using GridMeter.Application.Sites;
using MediatR;

namespace GridMeter.Api.Endpoints;

public sealed class ListSitesEndpoint(IMediator mediator) : EndpointWithoutRequest<IReadOnlyList<SiteDto>>
{
    public override void Configure() => Get("/sites");

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.OkAsync(await mediator.Send(new ListSitesQuery(), ct), ct);
}

public sealed class CreateSiteRequest
{
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";
}

public sealed class CreateSiteEndpoint(IMediator mediator) : Endpoint<CreateSiteRequest, SiteDto>
{
    public override void Configure() => Post("/sites");

    public override async Task HandleAsync(CreateSiteRequest req, CancellationToken ct) =>
        await Send.OkAsync(await mediator.Send(new CreateSiteCommand(req.Name, req.Region), ct), ct);
}

public sealed class SiteIdRequest
{
    public Guid Id { get; set; }
}

public sealed class GetSiteEndpoint(IMediator mediator) : Endpoint<SiteIdRequest, SiteDetailDto>
{
    public override void Configure() => Get("/sites/{id}");

    public override async Task HandleAsync(SiteIdRequest req, CancellationToken ct) =>
        await Send.OkAsync(await mediator.Send(new GetSiteQuery(req.Id), ct), ct);
}

public sealed class AddMeterRequest
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = "";
    public string Type { get; set; } = "Import";
}

public sealed class AddMeterEndpoint(IMediator mediator) : Endpoint<AddMeterRequest, MeterDto>
{
    public override void Configure() => Post("/sites/{id}/meters");

    public override async Task HandleAsync(AddMeterRequest req, CancellationToken ct) =>
        await Send.OkAsync(await mediator.Send(new AddMeterCommand(req.Id, req.ExternalId, req.Type), ct), ct);
}
