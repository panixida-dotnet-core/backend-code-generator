using System.Text.Json.Nodes;

namespace PANiXiDA.Core.Backend.CodeGenerator.UnitTests;

public sealed class ReadRepositoryTests
{
    [Theory(DisplayName = "Generate only supported read operations for aggregates and child entities")]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task GeneratesOnlyAvailableOperations(bool details, bool list)
    {
        using var backend = new ScratchBackend();
        var jobs = new List<JsonObject>
        {
            ScratchBackend.Job(
                "IReadRepository", "ReadRepository", "ReadDbModel",
                "ChildIReadRepository", "ChildReadRepository", "ChildReadDbModel")
        };
        if (details)
        {
            jobs.Add(ScratchBackend.Job(
                "DetailsReadModel", "DetailsReadModelMapper", "ChildDetailsReadModel", "ChildDetailsReadModelMapper"));
        }

        if (list)
        {
            jobs.Add(ScratchBackend.Job(
                "ListItemReadModel", "ListItemReadModelMapper", "ListItemReadModelSorting",
                "ChildListItemReadModel", "ChildListItemReadModelMapper", "ChildListItemReadModelSorting"));
        }

        var files = await backend.RunAsync([.. jobs]);

        var sources = files.Where(file => file.Path.EndsWith("ReadRepository.cs", StringComparison.Ordinal))
            .Select(file => backend.Read(file.Path))
            .ToArray();
        sources.Length.ShouldBe(4);
        foreach (var source in sources)
        {
            source.Contains(".Read.GetDetails;", StringComparison.Ordinal).ShouldBe(details);
            source.Contains("GetByIdAsync(", StringComparison.Ordinal).ShouldBe(details);
            source.Contains(".Read.GetList;", StringComparison.Ordinal).ShouldBe(list);
            source.Contains("GetPagedListAsync(", StringComparison.Ordinal).ShouldBe(list);
        }

        await backend.BuildLayerAsync("Infrastructure");
    }

    [Fact(DisplayName = "Use existing read models and omit read models deleted by another job")]
    public async Task UsesReadModelsThatRemainInTheRepository()
    {
        using var backend = new ScratchBackend();
        await backend.RunAsync(ScratchBackend.Job("DetailsReadModel", "ListItemReadModel"));
        var delete = ScratchBackend.Job("ListItemReadModel");
        delete["deleteGenerated"] = true;

        var files = await backend.RunAsync(ScratchBackend.Job("IReadRepository"), delete);

        var readRepository = files.Single(file => file.Action == GeneratedFileAction.Created);
        var source = backend.Read(readRepository.Path);
        source.ShouldContain("GetByIdAsync(");
        source.ShouldNotContain("GetPagedListAsync(");
        await backend.BuildLayerAsync("Application");
    }

    [Fact(DisplayName = "Generate a repository interface in a custom directory without an Application project")]
    public async Task SupportsCustomRepositoryDirectoriesWithoutApplication()
    {
        using var backend = new ScratchBackend();
        var module = ScratchBackend.ModuleName;
        File.Delete(backend.FullPath($"Modules/Module/{module}.Application/{module}.Application.csproj"));
        var job = ScratchBackend.Job("IReadRepository");
        job["outputDirectory"] = new JsonObject { ["IReadRepository"] = "output" };

        var files = await backend.RunAsync(job);

        var source = backend.Read(files.Single().Path);
        source.ShouldNotContain(".Read.GetDetails;");
        source.ShouldNotContain(".Read.GetList;");
    }
}
