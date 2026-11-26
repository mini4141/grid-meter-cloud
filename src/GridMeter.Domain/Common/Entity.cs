namespace GridMeter.Domain.Common;

public interface IDomainEvent;

public abstract class Entity
{
    private readonly List<IDomainEvent> _events = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    protected void Raise(IDomainEvent e) => _events.Add(e);

    public void ClearEvents() => _events.Clear();
}
