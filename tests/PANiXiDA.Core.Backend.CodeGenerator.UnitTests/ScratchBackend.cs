using System.Diagnostics;
using System.Text.Json.Nodes;

namespace PANiXiDA.Core.Backend.CodeGenerator.UnitTests;

internal sealed class ScratchBackend : IDisposable
{
    internal const string ModuleName = "Organization.Product.Module";
    internal const string DomainDirectory = $"Modules/Module/{ModuleName}.Domain";
    internal const string Product = $"{ModuleName}.Domain.Products.Product";
    internal const string Configuration = "tools/Generator/appsettings.json";

    private static readonly string s_fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Backend");
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("panixida-codegen-");

    internal ScratchBackend(bool withFixture = true)
    {
        if (withFixture)
        {
            foreach (var file in Directory.EnumerateFiles(s_fixture, "*", SearchOption.AllDirectories))
            {
                Write(Path.GetRelativePath(s_fixture, file), File.ReadAllText(file));
            }
        }
    }

    internal string Root => _directory.FullName;

    internal static JsonObject Job(params string[] artifacts)
    {
        var source = File.ReadAllText(Path.Combine(s_fixture, Configuration));
        var job = JsonNode.Parse(source)!["jobs"]!.AsArray()[0]!.AsObject();
        if (artifacts.Length > 0)
        {
            job["generatedFiles"] = new JsonArray([.. artifacts.Select(artifact => (JsonNode?)artifact)]);
        }

        return job;
    }

    internal Task<IReadOnlyList<GeneratedFile>> RunAsync(params JsonObject[] jobs)
    {
        var configuration = new JsonObject
        {
            ["jobs"] = new JsonArray([.. jobs.Select(job => job.DeepClone())])
        };
        Write(Configuration, configuration.ToJsonString());
        return BackendCodeGenerator.GenerateAsync(Root, Configuration, TestContext.Current.CancellationToken);
    }

    internal string FullPath(string relative) => Path.Combine(Root, relative);

    internal string Read(string path) => File.ReadAllText(FullPath(path));

    internal void Write(string path, string text)
    {
        var fullPath = FullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, text);
    }

    internal async Task BuildLayerAsync(string layer)
    {
        var project = $"Modules/Module/{ModuleName}.{layer}/{ModuleName}.{layer}.csproj";
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "build", project, "--nologo", "--verbosity", "quiet" }
        }) ?? throw new InvalidOperationException("Could not start dotnet build.");

        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        var buildLog = await output + await errors;
        process.ExitCode.ShouldBe(0, buildLog);
    }

    public void Dispose() => _directory.Delete(recursive: true);
}
