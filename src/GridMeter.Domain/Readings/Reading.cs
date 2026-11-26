namespace GridMeter.Domain.Readings;

public sealed class Reading(Guid meterId, DateTime intervalStart, decimal kwh)
{
    public Guid MeterId { get; private set; } = meterId;
    public DateTime IntervalStart { get; private set; } = intervalStart;
    public decimal Kwh { get; private set; } = kwh;
}
