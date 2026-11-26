using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.EntityFrameworkCore;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using GridMeter.Application.Abstractions;
using GridMeter.Application.Messaging;
using GridMeter.Domain.DemandResponse;
using GridMeter.Domain.Readings;
using GridMeter.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace GridMeter.Infrastructure.Persistence;

public sealed class AppDbContext(IMultiTenantContextAccessor accessor, DbContextOptions<AppDbContext> options)
    : MultiTenantDbContext(accessor, options), IAppDbContext
{
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Meter> Meters => Set<Meter>();
    public DbSet<Reading> Readings => Set<Reading>();
    public DbSet<DemandResponseEvent> DemandResponseEvents => Set<DemandResponseEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Site>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).ValueGeneratedNever();
            e.Property(s => s.Name).HasMaxLength(100);
            e.Property(s => s.Region).HasMaxLength(50);
            e.HasMany(s => s.Meters).WithOne().HasForeignKey(m => m.SiteId);
            e.Navigation(s => s.Meters).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Ignore(s => s.DomainEvents);
            e.IsMultiTenant();
        });

        b.Entity<Meter>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).ValueGeneratedNever();
            e.Property(m => m.ExternalId).HasMaxLength(30);
            e.Property(m => m.Type).HasConversion<string>().HasMaxLength(10);
            e.IsMultiTenant();
        });

        b.Entity<Reading>(e =>
        {
            e.HasKey(r => new { r.MeterId, r.IntervalStart });
            e.Property(r => r.Kwh).HasPrecision(18, 4);
            e.IsMultiTenant();
        });

        b.Entity<DemandResponseEvent>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Id).ValueGeneratedNever();
            e.Property(d => d.TargetKw).HasPrecision(18, 2);
            e.Property(d => d.Status).HasMaxLength(20);
            e.Ignore(d => d.DomainEvents);
            e.IsMultiTenant();
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.TenantId).HasMaxLength(64);
            e.Property(o => o.Type).HasMaxLength(100);
            e.HasIndex(o => o.ProcessedAt);
        });

        b.Entity<InboxMessage>(e =>
        {
            e.HasKey(i => i.MessageId);
            e.Property(i => i.MessageId).HasMaxLength(300);
        });
    }
}
