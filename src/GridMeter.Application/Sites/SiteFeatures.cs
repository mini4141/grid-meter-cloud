using FluentValidation;
using GridMeter.Application.Abstractions;
using GridMeter.Domain.Sites;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GridMeter.Application.Sites;

public sealed record SiteDto(Guid Id, string Name, string Region, int MeterCount);
public sealed record MeterDto(Guid Id, string ExternalId, string Type);
public sealed record SiteDetailDto(Guid Id, string Name, string Region, IReadOnlyList<MeterDto> Meters);

public sealed record CreateSiteCommand(string Name, string Region) : ICommand<SiteDto>;

public sealed class CreateSiteValidator : AbstractValidator<CreateSiteCommand>
{
    public CreateSiteValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Region).NotEmpty().MaximumLength(50);
    }
}

public sealed class CreateSiteHandler(IAppDbContext db) : IRequestHandler<CreateSiteCommand, SiteDto>
{
    public async Task<SiteDto> Handle(CreateSiteCommand request, CancellationToken ct)
    {
        var site = Site.Create(request.Name, request.Region);
        db.Sites.Add(site);
        await db.SaveChangesAsync(ct);
        return new SiteDto(site.Id, site.Name, site.Region, 0);
    }
}

public sealed record AddMeterCommand(Guid SiteId, string ExternalId, string Type) : ICommand<MeterDto>;

public sealed class AddMeterValidator : AbstractValidator<AddMeterCommand>
{
    public AddMeterValidator()
    {
        RuleFor(x => x.ExternalId).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Type).Must(t => Enum.TryParse<MeterType>(t, true, out _)).WithMessage("Type must be Import or Export.");
    }
}

public sealed class AddMeterHandler(IAppDbContext db) : IRequestHandler<AddMeterCommand, MeterDto>
{
    public async Task<MeterDto> Handle(AddMeterCommand request, CancellationToken ct)
    {
        var site = await db.Sites.Include(s => s.Meters).FirstOrDefaultAsync(s => s.Id == request.SiteId, ct)
                   ?? throw new KeyNotFoundException("Site not found");
        var meter = site.AddMeter(request.ExternalId, Enum.Parse<MeterType>(request.Type, true));
        await db.SaveChangesAsync(ct);
        return new MeterDto(meter.Id, meter.ExternalId, meter.Type.ToString());
    }
}

public sealed record ListSitesQuery : IQuery<IReadOnlyList<SiteDto>>;

public sealed class ListSitesHandler(IAppDbContext db) : IRequestHandler<ListSitesQuery, IReadOnlyList<SiteDto>>
{
    public async Task<IReadOnlyList<SiteDto>> Handle(ListSitesQuery request, CancellationToken ct) =>
        await db.Sites.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new SiteDto(s.Id, s.Name, s.Region, s.Meters.Count)).ToListAsync(ct);
}

public sealed record GetSiteQuery(Guid Id) : IQuery<SiteDetailDto>;

public sealed class GetSiteHandler(IAppDbContext db) : IRequestHandler<GetSiteQuery, SiteDetailDto>
{
    public async Task<SiteDetailDto> Handle(GetSiteQuery request, CancellationToken ct)
    {
        var s = await db.Sites.AsNoTracking().Include(x => x.Meters).FirstOrDefaultAsync(x => x.Id == request.Id, ct)
                ?? throw new KeyNotFoundException("Site not found");
        return new SiteDetailDto(s.Id, s.Name, s.Region,
            s.Meters.Select(m => new MeterDto(m.Id, m.ExternalId, m.Type.ToString())).ToList());
    }
}
