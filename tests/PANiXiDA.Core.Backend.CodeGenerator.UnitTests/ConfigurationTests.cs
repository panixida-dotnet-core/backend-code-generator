using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Configuration;

namespace PANiXiDA.Core.Backend.CodeGenerator.UnitTests;

public sealed class ConfigurationTests
{
    [Fact(DisplayName = "Treat an empty job list with JSON comments as a no-op")]
    public async Task AcceptsEmptyJobs()
    {
        using var backend = new ScratchBackend(withFixture: false);
        backend.Write(ScratchBackend.Configuration, "{ // No work requested\n \"jobs\": [] }");

        var files = await BackendCodeGenerator.GenerateAsync(
            backend.Root, ScratchBackend.Configuration, TestContext.Current.CancellationToken);

        files.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Reject the removed migration setting")]
    public async Task RejectsMigrationFlag()
    {
        using var backend = new ScratchBackend();
        var job = ScratchBackend.Job("CreateCommand");
        job["needMigrate"] = true;

        var exception = await Should.ThrowAsync<JsonException>(() => backend.RunAsync(job));

        exception.Message.ShouldContain("needMigrate");
    }

    [Fact(DisplayName = "Reject an empty repository root and configuration path")]
    public async Task RejectsEmptyPaths()
    {
        using var backend = new ScratchBackend(withFixture: false);
        var token = TestContext.Current.CancellationToken;

        var root = await Should.ThrowAsync<ArgumentException>(() =>
            BackendCodeGenerator.GenerateAsync(" ", ScratchBackend.Configuration, token));
        var configuration = await Should.ThrowAsync<ArgumentException>(() =>
            BackendCodeGenerator.GenerateAsync(backend.Root, " ", token));

        root.ParamName.ShouldBe("repositoryRoot");
        configuration.ParamName.ShouldBe("configurationPath");
    }

    [Fact(DisplayName = "Honor cancellation before reading configuration")]
    public async Task HonorsCancellation()
    {
        using var backend = new ScratchBackend(withFixture: false);

        await Should.ThrowAsync<OperationCanceledException>(() =>
            BackendCodeGenerator.GenerateAsync(backend.Root, ScratchBackend.Configuration, new CancellationToken(true)));
    }

    [Theory(DisplayName = "Generate from the tool project's appsettings and resolve local overrides")]
    [InlineData(null)]
    [InlineData("tools/Generator")]
    public async Task GeneratesFromProjectConfiguration(string? templateBaseDirectory)
    {
        using var backend = new ScratchBackend();
        var job = ScratchBackend.Job("CreateCommand", "ChildGetListEndpoint");
        job.Remove("overwriteExisting");
        job.Remove("deleteGenerated");
        var child = job["models"]![0]!["children"]![0]!.AsObject();
        child.Remove("createMethod");
        child.Remove("updateMethod");
        child.Remove("deleteMethod");
        job["templates"] = new JsonObject { ["CreateCommand"] = "Templates/Custom.sbn" };
        var templatePath = Path.Combine(templateBaseDirectory ?? string.Empty, "Templates", "Custom.sbn");
        backend.Write(templatePath, "// Configured {{ model.resource }}\n");
        var configuration = WriteProjectConfiguration(backend, job);

        var files = await BackendCodeGenerator.GenerateAsync(
            configuration, backend.Root, templateBaseDirectory, TestContext.Current.CancellationToken);

        files.Count.ShouldBe(2);
        var command = files.Single(file => Path.GetFileName(file.Path) == "CreateProductCommand.cs");
        backend.Read(command.Path).ShouldContain("// Configured Product");
        var endpoint = files.Single(file => Path.GetFileName(file.Path) == "GetProductVariantsListEndpoint.cs");
        backend.Read(endpoint.Path).ShouldContain("GetProductVariantsList");
    }

    [Fact(DisplayName = "Allow a caller to pass configuration alone")]
    public async Task AcceptsConfigurationWithoutExplicitPaths()
    {
        var configuration = new ConfigurationBuilder().Build();

        var files = await BackendCodeGenerator.GenerateAsync(
            configuration, cancellationToken: TestContext.Current.CancellationToken);

        files.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Report unsupported job keys when binding configuration")]
    public async Task RejectsUnknownBoundSettings()
    {
        using var backend = new ScratchBackend();
        var job = ScratchBackend.Job("CreateCommand");
        job["needMigrate"] = false;
        var configuration = WriteProjectConfiguration(backend, job);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => BackendCodeGenerator.GenerateAsync(
            configuration, backend.Root, cancellationToken: TestContext.Current.CancellationToken));

        exception.ToString().ShouldContain("needMigrate");
    }

    [Fact(DisplayName = "Reject a null configuration and honor cancellation before binding")]
    public async Task ValidatesBoundConfiguration()
    {
        var configuration = new ConfigurationBuilder().Build();

        await Should.ThrowAsync<ArgumentNullException>(() => BackendCodeGenerator.GenerateAsync(
            configuration: null!, cancellationToken: TestContext.Current.CancellationToken));
        await Should.ThrowAsync<OperationCanceledException>(() => BackendCodeGenerator.GenerateAsync(
            configuration, cancellationToken: new CancellationToken(true)));
    }

    private static IConfiguration WriteProjectConfiguration(ScratchBackend backend, JsonObject job)
    {
        var json = new JsonObject
        {
            ["jobs"] = new JsonArray(job.DeepClone()),
            ["Logging"] = new JsonObject { ["LogLevel"] = "Information" }
        };
        backend.Write(ScratchBackend.Configuration, json.ToJsonString());

        return new ConfigurationBuilder()
            .SetBasePath(backend.FullPath("tools/Generator"))
            .AddJsonFile("appsettings.json")
            .Build();
    }
}
