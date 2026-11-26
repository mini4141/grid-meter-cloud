// Local stand-in for the S3-triggered Lambda: polls the (moto) bucket prefix and runs the very same
// Function.ProcessAsync code the deployed Lambda uses. Objects are tracked in memory so each is parsed once.
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using GridMeter.MeterParser.Lambda;

var bucket = Environment.GetEnvironmentVariable("UPLOADS_BUCKET") ?? "gridmeter-uploads";
var queue = Environment.GetEnvironmentVariable("READINGS_QUEUE_NAME") ?? "gridmeter-readings";
var s3 = new AmazonS3Client();
var sqs = new AmazonSQSClient();
var function = new Function(s3, sqs, queue);
var seen = new HashSet<string>();

void Log(string m) => Console.WriteLine($"{{\"@t\":\"{DateTime.UtcNow:O}\",\"@m\":\"{m.Replace("\"", "'")}\",\"app\":\"parser-local-runner\"}}");

Log($"LocalRunner watching s3://{bucket}/uploads/");
while (true)
{
    try
    {
        try { await s3.PutBucketAsync(bucket); } catch (AmazonS3Exception) { /* already exists */ }
        await sqs.CreateQueueAsync(queue);
        var listed = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket, Prefix = "uploads/" });
        foreach (var o in listed.S3Objects ?? [])
        {
            if (!seen.Add($"{o.Key}:{o.ETag}")) continue;
            var sent = await function.ProcessAsync(bucket, o.Key, Log);
            Log($"Parsed {o.Key}: {sent} messages sent");
        }
    }
    catch (Exception ex) { Log($"Poll error: {ex.Message}"); }
    await Task.Delay(TimeSpan.FromSeconds(2));
}
