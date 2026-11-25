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

        var sources = files.Where(file => Path.GetFileName(file.Path).Contains("ReadRepository", StringComparison.Ordinal))
            .Select(file => backend.Read(file.Path))
            .ToArray();
        sources.Length.ShouldBe(4);
        sources.ShouldAllBe(source => source.Contains("partial ", StringComparison.Ordinal));
        sources.ShouldAllBe(source => !source.Contains(" ProductVariantsReadRepository", StringComparison.Ordinal));
        var parents = files.Where(file => Path.GetFileName(file.Path) is "IProductsReadRepository.cs" or "ProductsReadRepository.cs")
            .Select(file => backend.Read(file.Path));
        foreach (var source in parents)
        {
            source.Contains(".Read.GetDetails;", StringComparison.Ordinal).ShouldBe(details);
            source.Contains("GetByIdAsync(", StringComparison.Ordinal).ShouldBe(details);
            source.Contains(".Read.GetList;", StringComparison.Ordinal).ShouldBe(list);
            source.Contains("GetPagedListAsync(", StringComparison.Ordinal).ShouldBe(list);
        }

        var children = files.Where(file => file.Path.EndsWith("ReadRepository.ProductVariants.cs", StringComparison.Ordinal))
            .Select(file => backend.Read(file.Path))
            .ToArray();
        children.Length.ShouldBe(2);
        foreach (var source in children)
        {
            source.Contains("GetVariantsItemByIdAsync(", StringComparison.Ordinal).ShouldBe(details);
            source.Contains("GetVariantsPageAsync(", StringComparison.Ordinal).ShouldBe(list);
        }

        await backend.BuildLayerAsync("Infrastructure");
    }

    [Fact(DisplayName = "Add multiple child collections to an existing aggregate read repository")]
    public async Task ExtendsExistingRepositoryWithMultipleChildren()
    {
        using var backend = new ScratchBackend();
        var productPath = $"{ScratchBackend.DomainDirectory}/Products/Product.cs";
        var product = backend.Read(productPath).Replace(
            "public IReadOnlyCollection<ProductVariant> Variants => _variants;",
            """
            public IReadOnlyCollection<ProductVariant> Variants => _variants;
                public IReadOnlyCollection<ProductVariant> AlternateVariants => _variants;
            """,
            StringComparison.Ordinal);
        backend.Write(productPath, product);
        await backend.RunAsync(ScratchBackend.Job("IReadRepository", "ReadRepository", "ReadDbModel"));

        var job = ScratchBackend.Job(
            "ChildReadDbModel", "ChildIReadRepository", "ChildReadRepository",
            "ChildDetailsReadModel", "ChildDetailsReadModelMapper",
            "ChildListItemReadModel", "ChildListItemReadModelMapper", "ChildListItemReadModelSorting",
            "ChildFilter", "ChildFilterValidator");
        var children = job["models"]![0]!["children"]!.AsArray();
        var alternate = children[0]!.DeepClone().AsObject();
        alternate["collection"] = "AlternateVariants";
        alternate["route"] = "alternate-variants";
        children.Add(alternate);

        var files = await backend.RunAsync(job);

        var repositories = files.Where(file => Path.GetFileName(file.Path).Contains("ReadRepository.", StringComparison.Ordinal))
            .ToArray();
        repositories.Length.ShouldBe(4);
        repositories.ShouldAllBe(file => file.Path.Contains($"{Path.DirectorySeparatorChar}Products{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        repositories.Select(file => backend.Read(file.Path)).ShouldContain(source =>
            source.Contains("GetAlternateVariantsItemByIdAsync(", StringComparison.Ordinal)
            && source.Contains("GetAlternateVariantsPageAsync(", StringComparison.Ordinal));
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

    [Theory(DisplayName = "Generate filter artifacts independently and use search only for text projections")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GeneratesFiltersIndependentlyOfListArtifacts(bool hasSearchFields)
    {
        using var backend = new ScratchBackend();
        var filters = hasSearchFields ? ScratchBackend.Job("Filter") : ScratchBackend.Job("Filter", "FilterValidator");
        var list = ScratchBackend.Job("ListItemReadModel", "GetListQuery", "GetListQueryValidator");
        if (!hasSearchFields)
        {
            filters["models"]![0]!["listFields"] = new JsonArray("Id");
            list["models"]![0]!["listFields"] = new JsonArray("Id");
        }

        var filterFiles = await backend.RunAsync(filters);
        var listFiles = await backend.RunAsync(list);

        var filter = filterFiles.Single(file => Path.GetFileName(file.Path) == "ProductsFilter.cs");
        backend.Read(filter.Path).ShouldContain(hasSearchFields
            ? "public sealed record ProductsFilter(string? Search = null) : IFilter;"
            : "public sealed record ProductsFilter : IFilter;");
        filterFiles.Any(file => Path.GetFileName(file.Path) == "ProductsFilterValidator.cs").ShouldBe(!hasSearchFields);
        var validator = listFiles.Single(file => Path.GetFileName(file.Path) == "GetProductsListQueryValidator.cs");
        var source = backend.Read(validator.Path);
        source.Contains("RuleFor(query => query.Filter)", StringComparison.Ordinal).ShouldBe(hasSearchFields);
        source.ShouldNotContain("ProductsFilterValidator");

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
