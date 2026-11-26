using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.Docker;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.Docker.DockerTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build: Debug locally, Release in CI")]
    readonly string Configuration = IsLocalBuild ? "Debug" : "Release";

    [Parameter("Also run the Docker-based integration tests")]
    readonly bool WithIntegration = true;

    [Solution] readonly Solution Solution;

    AbsolutePath Artifacts => RootDirectory / "artifacts";
    AbsolutePath TestResults => RootDirectory / "TestResults";

    Target Restore => _ => _
        .Executes(() => DotNetRestore(s => s.SetProjectFile(Solution)));

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() => DotNetBuild(s => s
            .SetProjectFile(Solution)
            .SetConfiguration(Configuration)
            .EnableNoRestore()));

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var projects = new[] { "GridMeter.UnitTests", "GridMeter.ArchTests" };
            foreach (var p in projects) RunTests(p);
            if (WithIntegration) RunTests("GridMeter.IntegrationTests");
        });

    void RunTests(string project) =>
        DotNetTest(s => s
            .SetProjectFile(RootDirectory / "tests" / project)
            .SetConfiguration(Configuration)
            .EnableNoBuild()
            .SetLoggers($"junit;LogFilePath={TestResults / (project + ".xml")}", $"trx;LogFileName={project}.trx")
            .SetResultsDirectory(TestResults));

    Target Publish => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            foreach (var (project, output) in new[]
                     {
                         ("GridMeter.Api", "api"),
                         ("GridMeter.PriceMock", "pricemock"),
                         ("GridMeter.MeterParser.Lambda", "parser-lambda"),
                     })
            {
                DotNetPublish(s => s
                    .SetProject(RootDirectory / "src" / project)
                    .SetConfiguration("Release")
                    .SetOutput(Artifacts / output));
            }
        });

    Target DockerBuild => _ => _
        .Executes(() =>
        {
            foreach (var target in new[] { "api", "pricemock", "parser" })
            {
                DockerTasks.DockerBuild(s => s
                    .SetPath(".")
                    .SetTarget(target)
                    .SetTag($"gridmeter/{target}:local"));
            }
        });

    // Starts the API, downloads its OpenAPI document and generates the Kiota TypeScript client.
    Target GenerateClient => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var spec = RootDirectory / ".local" / "openapi.json";
            spec.Parent.CreateDirectory();
            var apiDll = RootDirectory / "src" / "GridMeter.Api" / "bin" / Configuration / "net10.0" / "GridMeter.Api.dll";
            var startInfo = new ProcessStartInfo("dotnet", $"\"{apiDll}\" --urls http://localhost:5199")
            {
                UseShellExecute = false,
                WorkingDirectory = apiDll.Parent,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                EnvironmentVariables =
                {
                    ["ASPNETCORE_ENVIRONMENT"] = "Production",
                    ["Messaging__Enabled"] = "false",
                },
            };
            using var api = Process.Start(startInfo)!;
            api.BeginOutputReadLine();
            api.BeginErrorReadLine();
            try
            {
                using var http = new HttpClient();
                string json = null;
                for (var i = 0; i < 60 && json == null && !api.HasExited; i++)
                {
                    try { json = http.GetStringAsync("http://localhost:5199/swagger/v1/swagger.json").GetAwaiter().GetResult(); }
                    catch { Thread.Sleep(1000); }
                }
                Assert.NotNull(json, "API did not publish an OpenAPI document");
                File.WriteAllText(spec, json);
            }
            finally
            {
                api.Kill(entireProcessTree: true);
            }

            var kiota = ToolResolver.GetPathTool("kiota");
            kiota($"generate -l typescript -d \"{spec}\" -c ApiClient -o \"{RootDirectory / "web" / "src" / "api" / "generated"}\" --clean-output --exclude-backward-compatible", RootDirectory);
        });
}
