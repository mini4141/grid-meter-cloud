using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using GridMeter.Application.Abstractions;
using GridMeter.Application.Readings;
using GridMeter.Infrastructure.Messaging;
using GridMeter.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Testcontainers.MsSql;

namespace GridMeter.IntegrationTests;

public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU16-ubuntu-22.04").Build();

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _sql.GetConnectionString());
        builder.UseSetting("Messaging:Enabled", "false");
        builder.UseSetting("Messaging:AutoCreateQueues", "false");
    }

    public async Task<HttpClient> ClientForAsync(string tenant)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant", tenant);
        var resp = await client.PostAsJsonAsync("/api/auth/token", new { tenant, user = "tester" });
        resp.EnsureSuccessStatusCode();
        var token = (await resp.Content.ReadFromJsonAsync<TokenDto>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }
}

public sealed record TokenDto(string Token);
public sealed record SiteDto(Guid Id, string Name, string Region, int MeterCount);
public sealed record IssuedDto(Guid EventId);

public class IntegrationTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Tenant_A_cannot_see_tenant_B_sites()
    {
        var aurora = await api.ClientForAsync("aurora-power");
        var kita = await api.ClientForAsync("kita-denki");

        (await aurora.PostAsJsonAsync("/api/sites", new { name = "Aurora Depot", region = "VIC" })).EnsureSuccessStatusCode();

        var auroraSites = await aurora.GetFromJsonAsync<List<SiteDto>>("/api/sites");
        var kitaSites = await kita.GetFromJsonAsync<List<SiteDto>>("/api/sites");

        auroraSites!.ShouldContain(s => s.Name == "Aurora Depot");
        kitaSites!.ShouldNotContain(s => s.Name == "Aurora Depot");
    }

    [Fact]
    public async Task Token_of_one_tenant_is_rejected_for_another()
    {
        var aurora = await api.ClientForAsync("aurora-power");
        aurora.DefaultRequestHeaders.Remove("X-Tenant");
        aurora.DefaultRequestHeaders.Add("X-Tenant", "kita-denki");

        (await aurora.GetAsync("/api/sites")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reprocessing_the_same_message_does_not_duplicate_readings()
    {
        var aurora = await api.ClientForAsync("aurora-power");
        var site = await (await aurora.PostAsJsonAsync("/api/sites", new { name = "Inbox Site", region = "NSW" }))
            .Content.ReadFromJsonAsync<SiteDto>();
        (await aurora.PostAsJsonAsync($"/api/sites/{site!.Id}/meters", new { externalId = "MPAN-INBOX", type = "Import" })).EnsureSuccessStatusCode();

        var rows = new[] { new ReadingRow("MPAN-INBOX", new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), 0.5m) };
        for (var i = 0; i < 2; i++)
        {
            using var scope = api.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantSwitcher>().Use("aurora-power");
            await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new IngestReadingBatchCommand("aurora-power:batch-1", rows));
        }

        var r = await aurora.GetAsync($"/api/sites/{site.Id}/consumption");
        r.IsSuccessStatusCode.ShouldBeTrue(await r.Content.ReadAsStringAsync());
        var consumption = await r.Content.ReadFromJsonAsync<ConsumptionDto>();
        consumption!.TotalKwh.ShouldBe(0.5m);
    }

    [Fact]
    public async Task Demand_response_is_published_through_the_outbox()
    {
        var aurora = await api.ClientForAsync("aurora-power");
        var site = await (await aurora.PostAsJsonAsync("/api/sites", new { name = "DR Site", region = "QLD" }))
            .Content.ReadFromJsonAsync<SiteDto>();
        var resp = await aurora.PostAsJsonAsync($"/api/sites/{site!.Id}/demand-response",
            new { windowStart = "2026-11-20T17:00:00Z", windowEnd = "2026-11-20T19:00:00Z", targetKw = 150 });
        resp.EnsureSuccessStatusCode();

        var sqs = Substitute.For<IAmazonSQS>();
        sqs.GetQueueUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new GetQueueUrlResponse { QueueUrl = "http://sqs/q" });
        var config = api.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var dispatcher = new OutboxDispatcher(api.Services.GetRequiredService<IServiceScopeFactory>(), sqs,
            new QueueResolver(sqs, config), NullLogger<OutboxDispatcher>.Instance);

        (await dispatcher.DispatchPendingAsync(default)).ShouldBeGreaterThan(0);

        await sqs.Received().SendMessageAsync(Arg.Is<SendMessageRequest>(r => r.MessageAttributes["type"].StringValue == "DemandResponseIssued"), Arg.Any<CancellationToken>());
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.OutboxMessages.CountAsync(o => o.ProcessedAt == null)).ShouldBe(0);
    }
}

public sealed record ConsumptionDto(decimal TotalKwh);
