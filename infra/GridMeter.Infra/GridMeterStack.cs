using System.Collections.Generic;
using System.IO;
using Amazon.CDK;
using Amazon.CDK.AWS.AppConfig;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.Ecr.Assets;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ECS.Patterns;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.S3.Notifications;
using Amazon.CDK.AWS.SecretsManager;
using Amazon.CDK.AWS.SQS;
using Constructs;
using LogGroupProps = Amazon.CDK.AWS.Logs.LogGroupProps;

namespace GridMeter.Infra;

public sealed class GridMeterStack : Stack
{
    private const string ReadingsQueueName = "gridmeter-readings";
    private const string DrQueueName = "gridmeter-dr-events";

    public GridMeterStack(Construct scope, string id, IStackProps props) : base(scope, id, props)
    {
        var root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));

        // ---- network: public subnets for Fargate (no NAT gateway), isolated subnets for the database ----
        var vpc = new Vpc(this, "Vpc", new VpcProps
        {
            MaxAzs = 2,
            NatGateways = 0,
            SubnetConfiguration = new ISubnetConfiguration[]
            {
                new SubnetConfiguration { Name = "public", SubnetType = SubnetType.PUBLIC, CidrMask = 24 },
                new SubnetConfiguration { Name = "db", SubnetType = SubnetType.PRIVATE_ISOLATED, CidrMask = 24 },
            },
        });

        // ---- SQS (Azure Storage Queues equivalent) ----
        var readingsDlq = new Queue(this, "ReadingsDlq", new QueueProps { QueueName = ReadingsQueueName + "-dlq" });
        var readingsQueue = new Queue(this, "ReadingsQueue", new QueueProps
        {
            QueueName = ReadingsQueueName,
            VisibilityTimeout = Duration.Seconds(60),
            DeadLetterQueue = new DeadLetterQueue { Queue = readingsDlq, MaxReceiveCount = 5 },
        });
        var drDlq = new Queue(this, "DrDlq", new QueueProps { QueueName = DrQueueName + "-dlq" });
        var drQueue = new Queue(this, "DrQueue", new QueueProps
        {
            QueueName = DrQueueName,
            VisibilityTimeout = Duration.Seconds(60),
            DeadLetterQueue = new DeadLetterQueue { Queue = drDlq, MaxReceiveCount = 5 },
        });

        // ---- S3 (Blob Storage equivalent) ----
        var uploads = new Bucket(this, "Uploads", new BucketProps
        {
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            RemovalPolicy = RemovalPolicy.DESTROY,
            AutoDeleteObjects = true,
            Cors = new ICorsRule[]
            {
                new CorsRule { AllowedMethods = new[] { HttpMethods.PUT }, AllowedOrigins = new[] { "*" }, AllowedHeaders = new[] { "*" } },
            },
        });

        // ---- Lambda (Azure Functions equivalent): S3-triggered meter-file parser ----
        var parserLogs = new LogGroup(this, "ParserLogs", new LogGroupProps
        {
            Retention = RetentionDays.ONE_DAY,
            RemovalPolicy = RemovalPolicy.DESTROY,
        });
        var parser = new Amazon.CDK.AWS.Lambda.Function(this, "MeterParser", new Amazon.CDK.AWS.Lambda.FunctionProps
        {
            Runtime = Runtime.DOTNET_10,
            Handler = "GridMeter.MeterParser.Lambda::GridMeter.MeterParser.Lambda.Function::Handler",
            Code = Code.FromAsset(Path.Combine(root, "artifacts", "parser-lambda")),
            MemorySize = 512,
            Timeout = Duration.Seconds(60),
            LogGroup = parserLogs,
            Environment = new Dictionary<string, string> { ["READINGS_QUEUE_NAME"] = ReadingsQueueName },
        });
        uploads.GrantRead(parser);
        readingsQueue.GrantSendMessages(parser);
        uploads.AddEventNotification(EventType.OBJECT_CREATED, new Amazon.CDK.AWS.S3.Notifications.LambdaDestination(parser),
            new NotificationKeyFilter { Prefix = "uploads/", Suffix = ".csv" });

        // ---- RDS for SQL Server Express (Azure SQL Database equivalent) ----
        var dbSecurityGroup = new SecurityGroup(this, "DbSg", new SecurityGroupProps { Vpc = vpc, Description = "SQL Server, app tier only" });
        var db = new DatabaseInstance(this, "Db", new DatabaseInstanceProps
        {
            Engine = DatabaseInstanceEngine.SqlServerEx(new SqlServerExInstanceEngineProps { Version = SqlServerEngineVersion.VER_16 }),
            InstanceType = Amazon.CDK.AWS.EC2.InstanceType.Of(InstanceClass.T3, InstanceSize.MICRO),
            Vpc = vpc,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED },
            SecurityGroups = new[] { dbSecurityGroup },
            MultiAz = false,
            AllocatedStorage = 20,
            StorageType = StorageType.GP2,
            Credentials = Credentials.FromGeneratedSecret("sqladmin"),
            BackupRetention = Duration.Days(0),
            DeletionProtection = false,
            RemovalPolicy = RemovalPolicy.DESTROY,
            PubliclyAccessible = false,
            DeleteAutomatedBackups = true,
        });

        // ---- Secrets Manager (Key Vault equivalent): JWT signing key (DB credentials come from the RDS secret) ----
        var jwtSecret = new Amazon.CDK.AWS.SecretsManager.Secret(this, "JwtKey", new Amazon.CDK.AWS.SecretsManager.SecretProps
        {
            GenerateSecretString = new SecretStringGenerator { PasswordLength = 48, ExcludePunctuation = true },
        });

        // ---- AppConfig (App Configuration equivalent): feature flag profile ----
        var appConfigApp = new CfnApplication(this, "FlagsApp", new CfnApplicationProps { Name = "grid-meter-cloud" });
        var appConfigEnv = new CfnEnvironment(this, "FlagsEnv", new CfnEnvironmentProps { ApplicationId = appConfigApp.Ref, Name = "showcase" });
        var flagsProfile = new CfnConfigurationProfile(this, "FlagsProfile", new CfnConfigurationProfileProps
        {
            ApplicationId = appConfigApp.Ref,
            Name = "feature-flags",
            LocationUri = "hosted",
            Type = "AWS.Freeform",
        });
        var flagsVersion = new CfnHostedConfigurationVersion(this, "FlagsV1", new CfnHostedConfigurationVersionProps
        {
            ApplicationId = appConfigApp.Ref,
            ConfigurationProfileId = flagsProfile.Ref,
            ContentType = "application/json",
            Content = "{\"FeatureManagement\":{\"tariff-estimates\":false}}",
        });
        var immediate = new CfnDeploymentStrategy(this, "Immediate", new CfnDeploymentStrategyProps
        {
            Name = "immediate",
            DeploymentDurationInMinutes = 0,
            FinalBakeTimeInMinutes = 0,
            GrowthFactor = 100,
            ReplicateTo = "NONE",
        });
        var firstDeployment = new Amazon.CDK.AWS.AppConfig.CfnDeployment(this, "FlagsDeploy", new Amazon.CDK.AWS.AppConfig.CfnDeploymentProps
        {
            ApplicationId = appConfigApp.Ref,
            EnvironmentId = appConfigEnv.Ref,
            ConfigurationProfileId = flagsProfile.Ref,
            ConfigurationVersion = flagsVersion.Ref,
            DeploymentStrategyId = immediate.Ref,
        });

        // ---- ECS on Fargate behind an ALB (App Service for containers equivalent) ----
        var cluster = new Cluster(this, "Cluster", new ClusterProps { Vpc = vpc });
        var apiLogs = new LogGroup(this, "ApiLogs", new LogGroupProps { Retention = RetentionDays.ONE_DAY, RemovalPolicy = RemovalPolicy.DESTROY });
        var priceLogs = new LogGroup(this, "PriceLogs", new LogGroupProps { Retention = RetentionDays.ONE_DAY, RemovalPolicy = RemovalPolicy.DESTROY });
        var migrateLogs = new LogGroup(this, "MigrateLogs", new LogGroupProps { Retention = RetentionDays.ONE_DAY, RemovalPolicy = RemovalPolicy.DESTROY });

        var apiAsset = new DockerImageAsset(this, "ApiImage", new DockerImageAssetProps
        {
            Directory = root,
            File = "Dockerfile",
            Target = "api",
            Platform = Platform_.LINUX_AMD64,
        });
        var priceAsset = new DockerImageAsset(this, "PriceImage", new DockerImageAssetProps
        {
            Directory = root,
            File = "Dockerfile",
            Target = "pricemock",
            Platform = Platform_.LINUX_AMD64,
        });

        var appEnv = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["AWS_REGION"] = Region,
            ["Messaging__ReadingsQueue"] = ReadingsQueueName,
            ["Messaging__DemandResponseQueue"] = DrQueueName,
            ["Storage__UploadsBucket"] = uploads.BucketName,
            ["Secrets__DbSecretArn"] = db.Secret!.SecretArn,
            ["Secrets__JwtSecretArn"] = jwtSecret.SecretArn,
            ["AppConfig__ApplicationId"] = appConfigApp.Ref,
            ["AppConfig__EnvironmentId"] = appConfigEnv.Ref,
            ["AppConfig__ProfileId"] = flagsProfile.Ref,
            ["PriceApi__BaseUrl"] = "http://localhost:5100",
            ["BUILD_SHA"] = System.Environment.GetEnvironmentVariable("BUILD_SHA") ?? "cloud",
        };

        var service = new ApplicationLoadBalancedFargateService(this, "Api", new ApplicationLoadBalancedFargateServiceProps
        {
            Cluster = cluster,
            Cpu = 512,
            MemoryLimitMiB = 1024,
            DesiredCount = 1,
            PublicLoadBalancer = true,
            AssignPublicIp = true,
            TaskSubnets = new SubnetSelection { SubnetType = SubnetType.PUBLIC },
            HealthCheckGracePeriod = Duration.Seconds(60),
            TaskImageOptions = new ApplicationLoadBalancedTaskImageOptions
            {
                Image = ContainerImage.FromDockerImageAsset(apiAsset),
                ContainerPort = 8080,
                Environment = appEnv,
                LogDriver = LogDrivers.AwsLogs(new AwsLogDriverProps { StreamPrefix = "api", LogGroup = apiLogs }),
            },
        });
        service.TargetGroup.ConfigureHealthCheck(new Amazon.CDK.AWS.ElasticLoadBalancingV2.HealthCheck { Path = "/healthz", Interval = Duration.Seconds(15) });
        service.TargetGroup.SetAttribute("deregistration_delay.timeout_seconds", "5");

        // Mock wholesale price API as a sidecar in the same task (localhost:5100). 50% of calls fail on purpose.
        service.TaskDefinition.AddContainer("pricemock", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromDockerImageAsset(priceAsset),
            Essential = false,
            Environment = new Dictionary<string, string> { ["FAIL_RATE"] = "0.5", ["ASPNETCORE_URLS"] = "http://+:5100" },
            Logging = LogDrivers.AwsLogs(new AwsLogDriverProps { StreamPrefix = "pricemock", LogGroup = priceLogs }),
        });
        service.Service.Node.AddDependency(firstDeployment);

        var taskRole = service.TaskDefinition.TaskRole;
        readingsQueue.GrantConsumeMessages(taskRole);
        drQueue.GrantSendMessages(taskRole);
        drQueue.GrantConsumeMessages(taskRole);
        uploads.GrantPut(taskRole);
        db.Secret.GrantRead(taskRole);
        jwtSecret.GrantRead(taskRole);
        taskRole.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "appconfig:StartConfigurationSession", "appconfig:GetLatestConfiguration" },
            Resources = new[] { "*" },
        }));
        taskRole.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "sqs:ListQueues" },
            Resources = new[] { "*" },
        }));
        db.Connections.AllowDefaultPortFrom(service.Service, "app tier to SQL Server");

        // One-off task definition used by deploy.sh to run the EF migrations.
        var migrateTask = new FargateTaskDefinition(this, "MigrateTask", new FargateTaskDefinitionProps { Cpu = 512, MemoryLimitMiB = 1024 });
        migrateTask.AddContainer("migrate", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromDockerImageAsset(apiAsset),
            Command = new[] { "--migrate" },
            Environment = appEnv,
            Logging = LogDrivers.AwsLogs(new AwsLogDriverProps { StreamPrefix = "migrate", LogGroup = migrateLogs }),
        });
        db.Secret.GrantRead(migrateTask.TaskRole);
        jwtSecret.GrantRead(migrateTask.TaskRole);
        migrateTask.TaskRole.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "appconfig:StartConfigurationSession", "appconfig:GetLatestConfiguration" },
            Resources = new[] { "*" },
        }));
        var migrateSg = new SecurityGroup(this, "MigrateSg", new SecurityGroupProps { Vpc = vpc, Description = "one-off migration task" });
        db.Connections.AllowDefaultPortFrom(migrateSg, "migration task to SQL Server");

        // ---- SPA on S3 + CloudFront; /api/* is routed to the ALB so the browser only talks to one HTTPS origin ----
        var spaBucket = new Bucket(this, "Spa", new BucketProps
        {
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            RemovalPolicy = RemovalPolicy.DESTROY,
            AutoDeleteObjects = true,
        });
        var spaRewrite = new Amazon.CDK.AWS.CloudFront.Function(this, "SpaRewrite", new Amazon.CDK.AWS.CloudFront.FunctionProps
        {
            Code = FunctionCode.FromInline(
                "function handler(event) { var r = event.request; if (r.uri.indexOf('.') === -1) { r.uri = '/index.html'; } return r; }"),
            Runtime = FunctionRuntime.JS_2_0,
        });
        var distribution = new Amazon.CDK.AWS.CloudFront.Distribution(this, "Cdn", new Amazon.CDK.AWS.CloudFront.DistributionProps
        {
            DefaultRootObject = "index.html",
            PriceClass = PriceClass.PRICE_CLASS_100,
            DefaultBehavior = new BehaviorOptions
            {
                Origin = S3BucketOrigin.WithOriginAccessControl(spaBucket),
                ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                FunctionAssociations = new IFunctionAssociation[]
                {
                    new FunctionAssociation { EventType = FunctionEventType.VIEWER_REQUEST, Function = spaRewrite },
                },
            },
            AdditionalBehaviors = new Dictionary<string, IBehaviorOptions>
            {
                ["/api/*"] = new BehaviorOptions
                {
                    Origin = new LoadBalancerV2Origin(service.LoadBalancer, new LoadBalancerV2OriginProps { ProtocolPolicy = OriginProtocolPolicy.HTTP_ONLY }),
                    ViewerProtocolPolicy = ViewerProtocolPolicy.HTTPS_ONLY,
                    AllowedMethods = AllowedMethods.ALLOW_ALL,
                    CachePolicy = CachePolicy.CACHING_DISABLED,
                    OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER,
                },
            },
        });

        // ---- outputs used by the deploy / destroy / proof scripts ----
        Out("CloudFrontUrl", "https://" + distribution.DistributionDomainName);
        Out("DistributionId", distribution.DistributionId);
        Out("AlbUrl", "http://" + service.LoadBalancer.LoadBalancerDnsName);
        Out("SpaBucket", spaBucket.BucketName);
        Out("UploadsBucket", uploads.BucketName);
        Out("ClusterName", cluster.ClusterName);
        Out("ServiceName", service.Service.ServiceName);
        Out("MigrateTaskDefinition", migrateTask.TaskDefinitionArn);
        Out("MigrateSecurityGroup", migrateSg.SecurityGroupId);
        Out("PublicSubnets", string.Join(",", System.Linq.Enumerable.Select(vpc.PublicSubnets, s => s.SubnetId)));
        Out("DbEndpoint", db.DbInstanceEndpointAddress);
        Out("DbSecretArn", db.Secret.SecretArn);
        Out("LambdaName", parser.FunctionName);
        Out("LambdaLogGroup", parserLogs.LogGroupName);
        Out("ApiLogGroup", apiLogs.LogGroupName);
        Out("PriceLogGroup", priceLogs.LogGroupName);
        Out("MigrateLogGroup", migrateLogs.LogGroupName);
        Out("ReadingsQueueName", ReadingsQueueName);
        Out("DrQueueName", DrQueueName);
        Out("AppConfigApplicationId", appConfigApp.Ref);
        Out("AppConfigEnvironmentId", appConfigEnv.Ref);
        Out("AppConfigProfileId", flagsProfile.Ref);
        Out("AppConfigStrategyId", immediate.Ref);
    }

    private void Out(string name, string value) => _ = new CfnOutput(this, name, new CfnOutputProps { Value = value });
}
