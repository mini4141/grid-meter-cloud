using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Finbuckle.MultiTenant;
using GridMeter.Application.Abstractions;
using GridMeter.Infrastructure.Messaging;
using GridMeter.Infrastructure.Persistence;
using GridMeter.Infrastructure.Prices;
using GridMeter.Infrastructure.Storage;
using GridMeter.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GridMeter.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(config.GetConnectionString("Default")));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddScoped<ITenantSwitcher, TenantSwitcher>();

        services.AddSingleton<IUploadUrlService, UploadUrlService>();
        services.AddPriceClient(config["PriceApi:BaseUrl"] ?? "http://localhost:5100");

        services.AddSingleton<IAmazonSQS>(_ =>
        {
            var sqsConfig = new AmazonSQSConfig();
            if (config.GetValue<bool>("Messaging:AutoCreateQueues")) sqsConfig.HttpClientFactory = new LocalHttpClientFactory();
            return new AmazonSQSClient(sqsConfig);
        });
        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(new AmazonS3Config { ForcePathStyle = true }));
        services.AddSingleton<QueueResolver>();

        if (config.GetValue("Messaging:Enabled", true))
        {
            services.AddHostedService<OutboxDispatcher>();
            services.AddHostedService<ReadingsConsumer>();
            services.AddHostedService<DemandResponseNotifier>();
        }
        return services;
    }

    /// <summary>Local stand-in provisioning (moto): queues, bucket and bucket CORS.</summary>
    public static async Task ProvisionLocalAwsAsync(this IServiceProvider sp, IConfiguration config)
    {
        if (!config.GetValue<bool>("Messaging:AutoCreateQueues")) return;
        var queues = sp.GetRequiredService<QueueResolver>();
        await queues.UrlAsync(queues.ReadingsQueue, default);
        await queues.UrlAsync(queues.DemandResponseQueue, default);

        var s3 = sp.GetRequiredService<IAmazonS3>();
        var bucket = config["Storage:UploadsBucket"] ?? "gridmeter-uploads";
        try { await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }); }
        catch (BucketAlreadyOwnedByYouException) { /* created by the parser runner */ }
        await s3.PutCORSConfigurationAsync(new PutCORSConfigurationRequest
        {
            BucketName = bucket,
            Configuration = new CORSConfiguration
            {
                Rules = [new CORSRule { AllowedMethods = ["PUT", "GET"], AllowedOrigins = ["*"], AllowedHeaders = ["*"] }]
            }
        });
    }
}
