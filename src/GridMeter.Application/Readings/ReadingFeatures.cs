using GridMeter.Application.Abstractions;
using GridMeter.Application.Messaging;
using GridMeter.Domain.Readings;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GridMeter.Application.Readings;

public sealed record ReadingRow(string MeterId, DateTime Timestamp, decimal Kwh);

/// <summary>Ingests one queue message. Idempotent through the inbox table.</summary>
public sealed record IngestReadingBatchCommand(string MessageId, IReadOnlyList<ReadingRow> Rows) : ICommand<int>;

public sealed class IngestReadingBatchHandler(IAppDbContext db) : IRequestHandler<IngestReadingBatchCommand, int>
{
    public async Task<int> Handle(IngestReadingBatchCommand request, CancellationToken ct)
    {
        if (await db.InboxMessages.AnyAsync(i => i.MessageId == request.MessageId, ct)) return 0;

        db.InboxMessages.Add(new InboxMessage { MessageId = request.MessageId });

        var externalIds = request.Rows.Select(r => r.MeterId).Distinct().ToList();
        var meters = await db.Meters.Where(m => externalIds.Contains(m.ExternalId))
            .ToDictionaryAsync(m => m.ExternalId, m => m.Id, ct);

        var added = 0;
        foreach (var row in request.Rows)
        {
            if (!meters.TryGetValue(row.MeterId, out var meterId)) continue;
            var start = DateTime.SpecifyKind(row.Timestamp, DateTimeKind.Utc);
            if (await db.Readings.AnyAsync(r => r.MeterId == meterId && r.IntervalStart == start, ct)) continue;
            db.Readings.Add(new Reading(meterId, start, row.Kwh));
            added++;
        }

        await db.SaveChangesAsync(ct);
        return added;
    }
}

public sealed record ConsumptionPoint(DateTime Day, decimal Kwh);
public sealed record MeterConsumption(string MeterId, decimal Kwh);
public sealed record ConsumptionDto(IReadOnlyList<ConsumptionPoint> Daily, IReadOnlyList<MeterConsumption> PerMeter, decimal TotalKwh, decimal PeakKw);

public sealed record GetConsumptionQuery(Guid SiteId, DateTime? From, DateTime? To) : IQuery<ConsumptionDto>;

public sealed class GetConsumptionHandler(IAppDbContext db) : IRequestHandler<GetConsumptionQuery, ConsumptionDto>
{
    public async Task<ConsumptionDto> Handle(GetConsumptionQuery request, CancellationToken ct)
    {
        var fromDate = request.From ?? new DateTime(2000, 1, 1);
        var toDate = request.To ?? new DateTime(2100, 1, 1);

        var rows = from r in db.Readings.AsNoTracking()
                   join m in db.Meters.AsNoTracking() on r.MeterId equals m.Id
                   where m.SiteId == request.SiteId && r.IntervalStart >= fromDate && r.IntervalStart < toDate
                   select new { m.ExternalId, r.IntervalStart, r.Kwh };

        var daily = await rows.GroupBy(r => r.IntervalStart.Date).OrderBy(g => g.Key)
            .Select(g => new ConsumptionPoint(g.Key, g.Sum(x => x.Kwh))).ToListAsync(ct);
        var perMeter = await rows.GroupBy(r => r.ExternalId).OrderBy(g => g.Key)
            .Select(g => new MeterConsumption(g.Key, g.Sum(x => x.Kwh))).ToListAsync(ct);
        // Peak demand: highest half-hour site total, converted from kWh to kW.
        var peakKwh = await rows.GroupBy(r => r.IntervalStart).Select(g => g.Sum(x => x.Kwh))
            .OrderByDescending(x => x).FirstOrDefaultAsync(ct);

        return new ConsumptionDto(daily, perMeter, daily.Sum(d => d.Kwh), peakKwh * 2);
    }
}

public sealed record EstimateDto(decimal TotalKwh, decimal PricePerKwh, decimal EstimatedCost, string Currency);

public sealed record GetEstimateQuery(Guid SiteId) : IQuery<EstimateDto>;

public sealed class GetEstimateHandler(IAppDbContext db, IPriceClient prices) : IRequestHandler<GetEstimateQuery, EstimateDto>
{
    public async Task<EstimateDto> Handle(GetEstimateQuery request, CancellationToken ct)
    {
        var site = await db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SiteId, ct)
                   ?? throw new KeyNotFoundException("Site not found");
        var total = await (from r in db.Readings.AsNoTracking()
                           join m in db.Meters.AsNoTracking() on r.MeterId equals m.Id
                           where m.SiteId == site.Id
                           select r.Kwh).SumAsync(ct);
        var price = await prices.GetPricePerKwhAsync(site.Region, ct);
        return new EstimateDto(total, price, Math.Round(total * price, 2), "AUD");
    }
}
