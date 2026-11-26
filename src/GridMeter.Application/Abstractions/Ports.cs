using GridMeter.Application.Messaging;
using GridMeter.Domain.DemandResponse;
using GridMeter.Domain.Readings;
using GridMeter.Domain.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GridMeter.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Site> Sites { get; }
    DbSet<Meter> Meters { get; }
    DbSet<Reading> Readings { get; }
    DbSet<DemandResponseEvent> DemandResponseEvents { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<InboxMessage> InboxMessages { get; }
    ChangeTracker ChangeTracker { get; }
    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface ICurrentTenant
{
    string? Id { get; }
}

/// <summary>Lets background workers run code on behalf of a tenant.</summary>
public interface ITenantSwitcher
{
    void Use(string tenantId);
}

public interface IPriceClient
{
    Task<decimal> GetPricePerKwhAsync(string region, CancellationToken ct);
}

public interface IUploadUrlService
{
    UploadTicket CreateTicket(string tenantId, string fileName);
}

public sealed record UploadTicket(string Url, string Key);
