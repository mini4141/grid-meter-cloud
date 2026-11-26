# Azure to AWS mapping

The target job description names an Azure stack. This project is built on AWS only, with the closest equivalent for every item.

| Azure (job post) | AWS equivalent used here | Where it lives |
|---|---|---|
| App Service for containers | **Amazon ECS on Fargate** behind an **ALB** | `infra/GridMeter.Infra` (ApiStack), `Dockerfile` target `api` |
| Azure Functions | **AWS Lambda** (.NET 10) with an **S3 trigger** | `src/GridMeter.MeterParser.Lambda`; locally `MeterParser.LocalRunner` runs the same code |
| Azure SQL Database | **Amazon RDS for SQL Server Express** | CDK `DatabaseInstance`; locally SQL Server 2022 container |
| Azure Storage Queues | **Amazon SQS** (with a DLQ) | `gridmeter-readings`, `gridmeter-dr-events` |
| Azure Blob Storage | **Amazon S3** | uploads bucket (presigned PUT), SPA bucket |
| Azure App Configuration + feature flags | **AWS AppConfig** + `Microsoft.FeatureManagement` | flag `tariff-estimates`; locally `appsettings` plus a runtime override |
| Azure Key Vault | **AWS Secrets Manager** | RDS credentials and the JWT signing key; locally `user-secrets` / env |
| Azure DevOps Boards | **GitHub Issues + GitHub Projects** | issues F1-F8 |
| Azure DevOps Pipelines | **GitHub Actions** calling **Nuke** | `.github/workflows/ci.yml`, `build/Build.cs` |
| Azure CDN / Static Web Apps | **S3 + CloudFront** | SPA hosting; CloudFront also routes `/api/*` to the ALB |
| Bicep / ARM | **AWS CDK v2 in C#** | `infra/GridMeter.Infra` |
