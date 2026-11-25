using System.Text.Json.Nodes;

using PANiXiDA.Core.Backend.CodeGenerator.Generation;
using PANiXiDA.Core.Backend.CodeGenerator.Templating;

namespace PANiXiDA.Core.Backend.CodeGenerator.UnitTests;

public sealed class PlanningTests
{
    [Theory(DisplayName = "Reject an invalid job before resolving any project")]
    [InlineData("null", "nonempty models and generatedFiles")]
    [InlineData("{}", "nonempty models and generatedFiles")]
    [InlineData("""{"models":[],"generatedFiles":["Endpoints"]}""", "nonempty models and generatedFiles")]
    [InlineData("""{"models":[{}]}""", "nonempty models and generatedFiles")]
    [InlineData("""{"models":[{}],"generatedFiles":[]}""", "nonempty models and generatedFiles")]
    [InlineData("""{"models":[{}],"generatedFiles":[null]}""", "Unknown generated file")]
    [InlineData("""{"models":[{}],"generatedFiles":["Endpoints"],"templates":{"Endpoints":" "}}""", "nonempty paths")]
    [InlineData("""{"models":[{}],"generatedFiles":["Endpoints"],"templates":{"Endpoints":"x.sbn"},"outputDirectory":{"Endpoints":""}}""", "nonempty paths")]
    public async Task RejectsInvalidJobs(string job, string message)
    {
        using var backend = new ScratchBackend(withFixture: false);
        backend.Write(ScratchBackend.Configuration, $$"""{"jobs":[{{job}}]}""");

        var error = await Should.ThrowAsync<InvalidOperationException>(() => BackendCodeGenerator.GenerateAsync(
            backend.Root, ScratchBackend.Configuration, TestContext.Current.CancellationToken));

        error.Message.ShouldContain(message);
    }

    [Theory(DisplayName = "Treat absent jobs as an empty configuration")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task AllowsAbsentJobs(string json)
    {
        using var backend = new ScratchBackend(withFixture: false);
        backend.Write(ScratchBackend.Configuration, json);

        var result = await BackendCodeGenerator.GenerateAsync(
            backend.Root, ScratchBackend.Configuration, TestContext.Current.CancellationToken);

        result.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Report a repository directory that does not exist")]
    public async Task RejectsMissingRepository()
    {
        using var backend = new ScratchBackend(withFixture: false);

        await Should.ThrowAsync<DirectoryNotFoundException>(() => BackendCodeGenerator.GenerateAsync(
            backend.FullPath("missing"), ScratchBackend.Configuration, TestContext.Current.CancellationToken));
    }

    [Theory(DisplayName = "Reject incomplete model and child entries")]
    [InlineData("null", "CreateCommand", "cannot contain null")]
    [InlineData("""{"aggregate":"Organization.Product.Module.Domain.Products.Product","children":[null]}""",
        "ChildCreateCommand", "valid collection and resource")]
    [InlineData("""{"aggregate":"Organization.Product.Module.Domain.Products.Product","children":[{"collection":"!"}]}""",
        "ChildCreateCommand", "valid collection and resource")]
    [InlineData("""{"aggregate":"Organization.Product.Module.Domain.Products.Product","children":[{"collection":"Variants","resource":"!"}]}""",
        "ChildCreateCommand", "valid collection and resource")]
    public async Task RejectsIncompleteEntries(string json, string artifact, string message)
    {
        using var backend = new ScratchBackend();
        var job = ScratchBackend.Job(artifact);
        job["models"] = new JsonArray(JsonNode.Parse(json));
        job["deleteGenerated"] = true;

        var error = await Should.ThrowAsync<InvalidOperationException>(() => backend.RunAsync(job));

        error.Message.ShouldContain(message);
    }

    [Fact(DisplayName = "Reject an existing directory at a requested file path")]
    public async Task RejectsDirectoryAtOutput()
    {
        using var backend = new ScratchBackend();
        var job = ScratchBackend.Job("CreateCommand");
        job["deleteGenerated"] = true;
        job["outputDirectory"] = new JsonObject { ["CreateCommand"] = "generated" };
        Directory.CreateDirectory(backend.FullPath("generated/CreateProductCommand.cs"));

        var error = await Should.ThrowAsync<InvalidOperationException>(() => backend.RunAsync(job));

        error.Message.ShouldContain("not a unique file path");
    }

    [Fact(DisplayName = "Permit the same route when jobs generate artifacts for the same aggregate")]
    public async Task ReusesAggregateRoute()
    {
        using var backend = new ScratchBackend();
        var first = ScratchBackend.Job("Endpoints");
        first["templates"] = new JsonObject { ["DtoRecord.sbn"] = "unused.sbn" };
        var second = ScratchBackend.Job("Endpoints");
        second["outputDirectory"] = new JsonObject { ["Endpoints"] = "generated" };

        var files = await backend.RunAsync(first, second);

        files.Count.ShouldBe(2);
    }

    [Theory(DisplayName = "Diagnose invalid Scriban member access and missing includes")]
    [InlineData("{{ model.unknown_property }}", "Could not render template")]
    [InlineData("{{ include \"Missing.sbn\" }}", "Could not render template")]
    public async Task RejectsInvalidTemplateExecution(string source, string message)
    {
        using var backend = new ScratchBackend();
        var job = ScratchBackend.Job("CreateCommand");
        job["templates"] = new JsonObject { ["CreateCommand"] = "Invalid.sbn" };
        backend.Write("tools/Generator/Invalid.sbn", source);

        var error = await Should.ThrowAsync<InvalidOperationException>(() => backend.RunAsync(job));

        error.Message.ShouldContain(message);
    }

    [Fact(DisplayName = "Index only recognized artifacts and reject duplicate resource names")]
    public void ValidatesEmbeddedCatalog()
    {
        var catalog = CrudGenerator.LoadCatalog(
            ["NoLayer.cs.sbn", "Other/File.cs.sbn", "Application/Create__Resource__Command.cs.sbn"]);

        catalog.Keys.ShouldBe(["CreateCommand", "ChildCreateCommand"], ignoreOrder: true);
        Should.Throw<InvalidOperationException>(() => CrudGenerator.LoadCatalog(
            ["Application/Create__Resource__Command.cs.sbn", "Presentation/Create__Resource__Command.cs.sbn"]));
        Should.Throw<InvalidOperationException>(() => EmbeddedTemplates.Read("missing.sbn"));
    }
}
