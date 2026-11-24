using System.Text.Json;

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
}
