using Amazon.S3;
using Amazon.S3.Model;
using GridMeter.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace GridMeter.Infrastructure.Storage;

public sealed class UploadUrlService : IUploadUrlService
{
    private readonly Lazy<AmazonS3Client> _s3;
    private readonly string _bucket;
    private readonly Amazon.S3.Protocol _protocol;

    public UploadUrlService(IConfiguration config)
    {
        _bucket = config["Storage:UploadsBucket"] ?? "gridmeter-uploads";
        _protocol = (config["Storage:PublicServiceUrl"] ?? "").StartsWith("http://") ? Amazon.S3.Protocol.HTTP : Amazon.S3.Protocol.HTTPS;
        var publicUrl = config["Storage:PublicServiceUrl"];
        var s3Config = new AmazonS3Config { ForcePathStyle = true };
        if (!string.IsNullOrEmpty(publicUrl))
        {
            s3Config.ServiceURL = publicUrl;
            s3Config.AuthenticationRegion = config["AWS:Region"] ?? "ap-southeast-2";
        }
        else
        {
            s3Config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(config["AWS:Region"] ?? "ap-southeast-2");
        }
        _s3 = new Lazy<AmazonS3Client>(() => new AmazonS3Client(s3Config));
    }

    public UploadTicket CreateTicket(string tenantId, string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        var key = $"uploads/{tenantId}/{Guid.NewGuid():N}/{safeName}";
        var url = _s3.Value.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            Protocol = _protocol,
            Expires = DateTime.UtcNow.AddMinutes(10),
            ContentType = "text/csv"
        });
        return new UploadTicket(url, key);
    }
}
