using GridMeter.Application.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GridMeter.Application.DemandResponse;

public sealed record DemandResponseDto(Guid Id, DateTime WindowStart, DateTime WindowEnd, decimal TargetKw, bool Published);

public sealed record ListDemandResponseQuery(Guid SiteId) : IQuery<IReadOnlyList<DemandResponseDto>>;

public sealed class ListDemandResponseHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<ListDemandResponseQuery, IReadOnlyList<DemandResponseDto>>
{
    public async Task<IReadOnlyList<DemandResponseDto>> Handle(ListDemandResponseQuery request, CancellationToken ct)
    {
        var events = await db.DemandResponseEvents.AsNoTracking().Where(e => e.SiteId == request.SiteId)
            .OrderByDescending(e => e.WindowStart).ToListAsync(ct);
        var processed = await db.OutboxMessages.AsNoTracking()
            .Where(o => o.TenantId == tenant.Id && o.ProcessedAt != null && o.Type == "DemandResponseIssued")
            .Select(o => o.Payload).ToListAsync(ct);
        return events.Select(e => new DemandResponseDto(e.Id, e.WindowStart, e.WindowEnd, e.TargetKw,
            processed.Any(p => p.Contains(e.Id.ToString(), StringComparison.OrdinalIgnoreCase)))).ToList();
    }
}
