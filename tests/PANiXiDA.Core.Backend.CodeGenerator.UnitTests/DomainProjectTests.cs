using System.Diagnostics;

namespace PANiXiDA.Core.Backend.CodeGenerator.UnitTests;

public sealed class DomainProjectTests
{
    [Theory(DisplayName = "Require a single existing Domain project")]
    [InlineData("empty")]
    [InlineData("wrong.csproj")]
    [InlineData("missing.Domain.csproj")]
    public void RejectsInvalidProjectPaths(string path)
    {
        using var backend = new ScratchBackend(withFixture: false);
        Directory.CreateDirectory(backend.FullPath("empty"));
        backend.Write("wrong.csproj", "");
        var layout = new RepositoryLayout(backend.Root, backend.Root);

        var error = Should.Throw<InvalidOperationException>(() => DomainProject.Find(layout, path));

        error.Message.ShouldContain("exactly one .Domain.csproj");
    }

    [Theory(DisplayName = "Require an aggregate name that belongs to the selected Domain project")]
    [InlineData(null)]
    [InlineData("Other.Domain.Products.Product")]
    [InlineData("Sample.Domain.Product")]
    [InlineData("Sample.Domain.Products.1Product")]
    public void RejectsInvalidTypeNames(string? name)
    {
        var project = new DomainProject("Sample.Domain.csproj", "Sample", "Sample");

        Should.Throw<InvalidOperationException>(() => project.Locate(name!));
    }

    [Theory(DisplayName = "Use an absolute SDK host path on Windows and Unix")]
    [InlineData(null, true, "dotnet.exe")]
    [InlineData("relative/dotnet", false, "dotnet")]
    public void ResolvesHostFromRuntime(string? configured, bool isWindows, string hostName)
    {
        using var backend = new ScratchBackend(withFixture: false);

        var host = DomainProject.ResolveDotnetHost(configured, backend.FullPath("shared/Microsoft.NETCore.App/10.0"),
            isWindows);

        host.ShouldBe(backend.FullPath(hostName));
    }

    [Fact(DisplayName = "Prefer the absolute SDK host provided by the caller")]
    public void UsesConfiguredHost()
    {
        using var backend = new ScratchBackend(withFixture: false);
        var configured = backend.FullPath("sdk/dotnet");

        DomainProject.ResolveDotnetHost(configured, backend.FullPath("shared/runtime/version"), false).ShouldBe(configured);
    }

    [Fact(DisplayName = "Report build errors from the Domain project")]
    public async Task ReportsBuildFailure()
    {
        using var backend = new ScratchBackend(withFixture: false);
        backend.Write("Sample.Domain.csproj", "<Project>");
        var project = new DomainProject(backend.FullPath("Sample.Domain.csproj"), "Sample", "Sample");

        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            project.BuildAsync(backend.Root, TestContext.Current.CancellationToken));

        error.Message.ShouldContain("Could not build");
    }

    [Theory(DisplayName = "Require a full path to an existing build output")]
    [InlineData("")]
    [InlineData("relative.dll")]
    [InlineData("missing")]
    public void RejectsInvalidBuildOutput(string input)
    {
        using var backend = new ScratchBackend(withFixture: false);
        var project = new DomainProject(backend.FullPath("Sample.Domain.csproj"), "Sample", "Sample");
        var output = input == "missing" ? backend.FullPath("missing.dll") : input;

        Should.Throw<InvalidOperationException>(() => project.FindTargetPath(output));
    }

    [Fact(DisplayName = "Release a load context when the build output is not a .NET assembly")]
    public void RejectsInvalidAssembly()
    {
        using var backend = new ScratchBackend(withFixture: false);
        backend.Write("bad.dll", "not an assembly");

        Should.Throw<BadImageFormatException>(() => DomainProject.LoadAssembly(backend.FullPath("bad.dll")));
    }

    [Fact(DisplayName = "Terminate a build when the caller cancels it")]
    public async Task CancelsBuild()
    {
        using var backend = new ScratchBackend(withFixture: false);
        backend.Write("Sample.Domain.csproj", """<Project Sdk="Microsoft.NET.Sdk" />""");
        var project = new DomainProject(backend.FullPath("Sample.Domain.csproj"), "Sample", "Sample");

        await Should.ThrowAsync<OperationCanceledException>(() =>
            project.BuildAsync(backend.Root, new CancellationToken(canceled: true)));
    }

    [Fact(DisplayName = "Permit a build process that has already exited")]
    public async Task PermitsAlreadyExitedProcess()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            ArgumentList = { "--version" }
        })!;

        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        await DomainProject.StopBuildAsync(process);

        process.ExitCode.ShouldBe(0);
    }
}
