namespace GridMeter.Application.Messaging;

public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
}

public sealed class InboxMessage
{
    public string MessageId { get; set; } = "";
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
