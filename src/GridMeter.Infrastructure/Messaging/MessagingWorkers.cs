using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using GridMeter.Application.Abstractions;
using GridMeter.Application.Readings;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GridMeter.Infrastructure.Messaging;

/// <summary>Resolves (and locally creates) the SQS queues.</summary>
public sealed class QueueResolver(IAmazonSQS sqs, IConfiguration config)
{
    private readonly Dictionary<string, string> _urls = [];

    public async Task<string> UrlAsync(string name, CancellationToken ct)
    {
        if (_urls.TryGetValue(name, out var cached)) return cached;
        var url = config.GetValue<bool>("Messaging:AutoCreateQueues")
            ? (await sqs.CreateQueueAsync(name, ct)).QueueUrl
            : (await sqs.GetQueueUrlAsync(name, ct)).QueueUrl;
        return _urls[name] = url;
    }

    public string ReadingsQueue => config["Messaging:ReadingsQueue"] ?? "gridmeter-readings";
    public string DemandResponseQueue => config["Messaging:DemandResponseQueue"] ?? "gridmeter-dr-events";
}

/// <summary>Transactional outbox dispatcher: publishes unprocessed outbox rows to SQS.</summary>
public sealed class OutboxDispatcher(IServiceScopeFactory scopes, IAmazonSQS sqs, QueueResolver queues, ILogger<OutboxDispatcher> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await DispatchPendingAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Outbox dispatch failed"); }
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }

    public async Task<int> DispatchPendingAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var pending = await db.OutboxMessages.Where(o => o.ProcessedAt == null).OrderBy(o => o.OccurredAt).Take(20).ToListAsync(ct);
        if (pending.Count == 0) return 0;

        var url = await queues.UrlAsync(queues.DemandResponseQueue, ct);
        foreach (var m in pending)
        {
            await sqs.SendMessageAsync(new SendMessageRequest
            {
                QueueUrl = url,
                MessageBody = m.Payload,
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["tenant"] = new MessageAttributeValue { DataType = "String", StringValue = m.TenantId },
                    ["type"] = new MessageAttributeValue { DataType = "String", StringValue = m.Type },
                    ["outbox-id"] = new MessageAttributeValue { DataType = "String", StringValue = m.Id.ToString() }
                }
            }, ct);
            m.ProcessedAt = DateTime.UtcNow;
            log.LogInformation("Outbox message {OutboxId} ({Type}) dispatched to SQS for tenant {Tenant}", m.Id, m.Type, m.TenantId);
        }
        await db.SaveChangesAsync(ct);
        return pending.Count;
    }
}

/// <summary>Consumes reading batches produced by the parser Lambda and ingests them idempotently.</summary>
public sealed class ReadingsConsumer(IServiceScopeFactory scopes, IAmazonSQS sqs, QueueResolver queues, ILogger<ReadingsConsumer> log)
    : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var url = await queues.UrlAsync(queues.ReadingsQueue, ct);
                var resp = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = url,
                    MaxNumberOfMessages = 10,
                    WaitTimeSeconds = 5,
                    MessageAttributeNames = ["All"]
                }, ct);
                foreach (var msg in resp.Messages ?? [])
                {
                    await HandleAsync(msg, ct);
                    await sqs.DeleteMessageAsync(url, msg.ReceiptHandle, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Readings consumer error");
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }

    private async Task HandleAsync(Message msg, CancellationToken ct)
    {
        var tenant = msg.MessageAttributes["tenant"].StringValue;
        var batchId = msg.MessageAttributes["batch-id"].StringValue;
        var rows = JsonSerializer.Deserialize<List<ReadingRow>>(msg.Body, Json)!;

        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantSwitcher>().Use(tenant);
        var added = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new IngestReadingBatchCommand($"{tenant}:{batchId}", rows), ct);
        log.LogInformation("Ingested batch {BatchId} for tenant {Tenant}: {Added} readings added", batchId, tenant, added);
    }
}

/// <summary>Consumes demand-response events and "notifies the customer" (log only).</summary>
public sealed class DemandResponseNotifier(IAmazonSQS sqs, QueueResolver queues, ILogger<DemandResponseNotifier> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var url = await queues.UrlAsync(queues.DemandResponseQueue, ct);
                var resp = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = url,
                    MaxNumberOfMessages = 10,
                    WaitTimeSeconds = 5,
                    MessageAttributeNames = ["All"]
                }, ct);
                foreach (var msg in resp.Messages ?? [])
                {
                    log.LogInformation("Customer notified: tenant={Tenant} type={Type} payload={Payload}",
                        msg.MessageAttributes["tenant"].StringValue, msg.MessageAttributes["type"].StringValue, msg.Body);
                    await sqs.DeleteMessageAsync(url, msg.ReceiptHandle, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Notifier error");
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }
}
