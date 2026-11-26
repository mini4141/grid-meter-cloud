using GridMeter.Domain.Common;

namespace GridMeter.Domain.DemandResponse;

public sealed record DemandResponseIssued(Guid EventId, Guid SiteId, DateTime WindowStart, DateTime WindowEnd, decimal TargetKw) : IDomainEvent;

public sealed class DemandResponseEvent : Entity
{
    private DemandResponseEvent() { }

    public Guid Id { get; private set; }
    public Guid SiteId { get; private set; }
    public DateTime WindowStart { get; private set; }
    public DateTime WindowEnd { get; private set; }
    public decimal TargetKw { get; private set; }
    public string Status { get; private set; } = "Issued";

    public static DemandResponseEvent Issue(Guid siteId, DateTime windowStart, DateTime windowEnd, decimal targetKw)
    {
        var e = new DemandResponseEvent
        {
            Id = Guid.NewGuid(), SiteId = siteId, WindowStart = windowStart, WindowEnd = windowEnd, TargetKw = targetKw
        };
        e.Raise(new DemandResponseIssued(e.Id, siteId, windowStart, windowEnd, targetKw));
        return e;
    }
}
