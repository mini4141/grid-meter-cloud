using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.SQS;
using Amazon.SQS.Model;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace GridMeter.MeterParser.Lambda;

/// <summary>Triggered by S3 ObjectCreated on `uploads/{tenant}/{id}/{file}.csv`. Sends reading batches to SQS.</summary>
public sealed class Function
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IAmazonS3 _s3;
    private readonly IAmazonSQS _sqs;
    private readonly string _queueName;

    public Function() : this(new AmazonS3Client(), new AmazonSQSClient(),
        Environment.GetEnvironmentVariable("READINGS_QUEUE_NAME") ?? "gridmeter-readings") { }

    public Function(IAmazonS3 s3, IAmazonSQS sqs, string queueName)
    {
        _s3 = s3;
        _sqs = sqs;
        _queueName = queueName;
    }

    public async Task Handler(S3Event e, ILambdaContext context)
    {
        foreach (var record in e.Records)
        {
            var bucket = record.S3.Bucket.Name;
            var key = Uri.UnescapeDataString(record.S3.Object.Key.Replace('+', ' '));
            var sent = await ProcessAsync(bucket, key, context.Logger.LogInformation);
            context.Logger.LogInformation($"Parsed s3://{bucket}/{key}: {sent} messages sent");
        }
    }

    public async Task<int> ProcessAsync(string bucket, string key, Action<string> log)
    {
        // key = uploads/{tenant}/{uploadId}/{file}
        var segments = key.Split('/');
        if (segments.Length < 4 || segments[0] != "uploads") { log($"Ignoring key {key}"); return 0; }
        var tenant = segments[1];

        using var obj = await _s3.GetObjectAsync(bucket, key);
        using var reader = new StreamReader(obj.ResponseStream);
        var parsed = await MeterFileParser.ParseAsync(reader);
        log($"Parsed {parsed.Rows.Count} rows ({parsed.Rejected} rejected) for tenant {tenant}");

        var queueUrl = (await _sqs.GetQueueUrlAsync(_queueName)).QueueUrl;
        var batches = MeterFileParser.Batch(parsed.Rows).Select((rows, i) => (rows, id: $"{segments[2]}-{i}")).ToList();

        foreach (var chunk in batches.Chunk(10))
        {
            var entries = chunk.Select(b => new SendMessageBatchRequestEntry
            {
                Id = b.id,
                MessageBody = JsonSerializer.Serialize(b.rows, Json),
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["tenant"] = new MessageAttributeValue { DataType = "String", StringValue = tenant },
                    ["batch-id"] = new MessageAttributeValue { DataType = "String", StringValue = b.id }
                }
            }).ToList();
            var resp = await _sqs.SendMessageBatchAsync(queueUrl, entries);
            if (resp.Failed.Count > 0) throw new InvalidOperationException($"{resp.Failed.Count} batch entries failed to send");
        }
        log($"Sent {batches.Count} SQS messages to {_queueName}");
        return batches.Count;
    }
}
