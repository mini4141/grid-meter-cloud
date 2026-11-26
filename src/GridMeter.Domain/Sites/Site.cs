using GridMeter.Domain.Common;

namespace GridMeter.Domain.Sites;

public sealed class Site : Entity
{
    private readonly List<Meter> _meters = [];

    private Site() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Region { get; private set; } = "";
    public IReadOnlyList<Meter> Meters => _meters;

    public static Site Create(string name, string region) =>
        new() { Id = Guid.NewGuid(), Name = name.Trim(), Region = region.Trim() };

    public Meter AddMeter(string externalId, MeterType type)
    {
        var meter = new Meter(Guid.NewGuid(), Id, externalId.Trim(), type);
        _meters.Add(meter);
        return meter;
    }
}

public enum MeterType { Import = 0, Export = 1 }

public sealed class Meter(Guid id, Guid siteId, string externalId, MeterType type)
{
    public Guid Id { get; private set; } = id;
    public Guid SiteId { get; private set; } = siteId;
    public string ExternalId { get; private set; } = externalId;
    public MeterType Type { get; private set; } = type;
}
