using Amazon.S3;
using Amazon.S3.Model;
using GridMeter.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace GridMeter.Infrastructure.Storage;

public sealed class UploadUrlService : IUploadUrlService
{
    private readonly Lazy<AmazonS3Client> _s3;
    private readonly string _bucket;

    public UploadUrlService(IConfiguration config)
    {
        _bucket = config["Storage:UploadsBucket"] ?? "gridmeter-uploads";
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
            Expires = DateTime.UtcNow.AddMinutes(10),
            ContentType = "text/csv"
        });
        return new UploadTicket(url, key);
    }
}
