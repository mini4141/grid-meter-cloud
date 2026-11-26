using GridMeter.Application.Abstractions;
using GridMeter.Application.Messaging;
using GridMeter.Application.Sites;
using GridMeter.Domain.DemandResponse;
using GridMeter.Domain.Readings;
using GridMeter.Domain.Sites;
using GridMeter.MeterParser.Lambda;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace GridMeter.UnitTests;

public sealed class TestDb(DbContextOptions<TestDb> options) : DbContext(options), IAppDbContext
{
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Meter> Meters => Set<Meter>();
    public DbSet<Reading> Readings => Set<Reading>();
    public DbSet<DemandResponseEvent> DemandResponseEvents => Set<DemandResponseEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Site>().Ignore(s => s.DomainEvents).HasMany(s => s.Meters).WithOne().HasForeignKey(m => m.SiteId);
        b.Entity<Site>().Navigation(s => s.Meters).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Entity<Meter>();
        b.Entity<Reading>().HasKey(r => new { r.MeterId, r.IntervalStart });
        b.Entity<DemandResponseEvent>().Ignore(d => d.DomainEvents);
        b.Entity<OutboxMessage>();
        b.Entity<InboxMessage>().HasKey(i => i.MessageId);
    }

    public static TestDb Create() =>
        new(new DbContextOptionsBuilder<TestDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}

public class CreateSiteTests
{
    [Fact]
    public async Task Handler_persists_a_site()
    {
        await using var db = TestDb.Create();
        var result = await new CreateSiteHandler(db).Handle(new CreateSiteCommand("Melbourne Depot", "VIC"), default);

        result.Name.ShouldBe("Melbourne Depot");
        (await db.Sites.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public void Validator_rejects_an_empty_name()
    {
        var result = new CreateSiteValidator().Validate(new CreateSiteCommand("", "VIC"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == "Name");
    }
}

public class DemandResponseTests
{
    [Fact]
    public void Issuing_raises_a_domain_event()
    {
        var start = new DateTime(2026, 11, 20, 17, 0, 0, DateTimeKind.Utc);
        var dr = DemandResponseEvent.Issue(Guid.NewGuid(), start, start.AddHours(2), 150);

        var raised = dr.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<DemandResponseIssued>();
        raised.TargetKw.ShouldBe(150);
    }
}

public class MeterFileParserTests
{
    [Fact]
    public async Task Parses_valid_rows_and_rejects_bad_ones()
    {
        const string csv = "meter_id,timestamp,kwh\nMPAN-1,2026-11-01T00:00:00Z,0.42\nMPAN-1,2026-11-01T00:30:00Z,0.40\nMPAN-1,bad,x\n";

        var result = await MeterFileParser.ParseAsync(new StringReader(csv));

        result.Rows.Count.ShouldBe(2);
        result.Rejected.ShouldBe(1);
        result.Rows[0].Kwh.ShouldBe(0.42m);
    }

    [Fact]
    public void Batches_rows_in_chunks_of_100()
    {
        var rows = Enumerable.Range(0, 250).Select(i => new ParsedReading("M", DateTime.UtcNow.AddMinutes(i), 1)).ToList();

        MeterFileParser.Batch(rows).Select(b => b.Count).ShouldBe([100, 100, 50]);
    }
}
