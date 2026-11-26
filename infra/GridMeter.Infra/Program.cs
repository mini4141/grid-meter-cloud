using Amazon.CDK;

namespace GridMeter.Infra;

internal static class Program
{
    public static void Main(string[] args)
    {
        var app = new App();

        _ = new GridMeterStack(app, "GridMeter", new StackProps
        {
            Env = new Amazon.CDK.Environment
            {
                Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
                Region = "ap-southeast-2",
            },
            Synthesizer = new DefaultStackSynthesizer(new DefaultStackSynthesizerProps { Qualifier = "gridmtr" }),
        });

        // Every resource in the app is tagged so verify-destroyed.sh can find leftovers.
        Tags.Of(app).Add("Project", "grid-meter-cloud");
        Tags.Of(app).Add("Ephemeral", "true");
        Tags.Of(app).Add("ManagedBy", "cdk");
        Tags.Of(app).Add("Owner", "alvieirvansyah");

        app.Synth();
    }
}
