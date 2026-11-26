using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Primitives;

namespace GridMeter.Api.Configuration;

public static class AwsConfiguration
{
    /// <summary>In AWS: DB connection and JWT key come from Secrets Manager, flags from AppConfig. Locally: nothing happens.</summary>
    public static void AddAwsConfiguration(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;

        var dbSecret = config["Secrets:DbSecretArn"];
        var jwtSecret = config["Secrets:JwtSecretArn"];
        if (!string.IsNullOrEmpty(dbSecret) || !string.IsNullOrEmpty(jwtSecret))
        {
            using var sm = new AmazonSecretsManagerClient();
            if (!string.IsNullOrEmpty(dbSecret))
            {
                var json = JsonDocument.Parse(Get(sm, dbSecret)).RootElement;
                config["ConnectionStrings:Default"] =
                    $"Server={json.GetProperty("host").GetString()},{json.GetProperty("port").GetInt32()};Database=GridMeter;" +
                    $"User Id={json.GetProperty("username").GetString()};Password={json.GetProperty("password").GetString()};TrustServerCertificate=True";
            }
            if (!string.IsNullOrEmpty(jwtSecret)) config["Jwt:Key"] = Get(sm, jwtSecret);
        }

        var app = config["AppConfig:ApplicationId"];
        if (!string.IsNullOrEmpty(app))
        {
            config.AddAppConfig(app, config["AppConfig:EnvironmentId"]!, config["AppConfig:ProfileId"]!, false,
                TimeSpan.FromSeconds(15));
        }
    }

    private static string Get(IAmazonSecretsManager sm, string id) =>
        sm.GetSecretValueAsync(new GetSecretValueRequest { SecretId = id }).GetAwaiter().GetResult().SecretString;
}

/// <summary>Lets the admin page flip a feature flag at runtime when AppConfig is not in play (local dev).</summary>
public sealed class RuntimeOverridesProvider : ConfigurationProvider
{
    public void Override(string key, string value)
    {
        Data[key] = value;
        OnReload();
    }
}

public sealed class RuntimeOverridesSource(RuntimeOverridesProvider provider) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
}
