using System.Text.Json.Nodes;

namespace PANiXiDA.Core.Backend.CodeGenerator.UnitTests;

public sealed class GenerationTests
{
    [Fact(DisplayName = "Generate and compile all aggregate and child CRUD files using only embedded templates")]
    public async Task GeneratesFullCrud()
    {
        using var backend = new ScratchBackend();

        var files = await backend.RunAsync(ScratchBackend.Job());

        files.Count.ShouldBe(80);
        files.Select(file => file.Path).Distinct().Count().ShouldBe(files.Count);
        files.ShouldAllBe(file => file.Action == GeneratedFileAction.Created);
        var code = files.Select(file => backend.Read(file.Path)).ToArray();
        code.ShouldContain(source => source.Contains("Product.Create(", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("ProductKind.Create", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("Money.Create(", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("Money? listPriceValue = null", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("parent.AddVariant(", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("parent.UpdateVariant(", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("parent.RemoveVariant(", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("item.ParentId == parentId && item.Id == id", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("GetPagedResultAsync", StringComparison.Ordinal)
            && source.Contains("Query.Where(item => item.ParentId == parentId)", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("products/{productId:guid}/variants", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("OwnsMany(item => item.Variants", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("HasColumnType(\"jsonb\")", StringComparison.Ordinal));
        Directory.EnumerateFiles(backend.Root, "*.sbn", SearchOption.AllDirectories).ShouldBeEmpty();

        await backend.BuildLayerAsync("Infrastructure");
        await backend.BuildLayerAsync("Presentation");
    }

    [Fact(DisplayName = "Generate only the selected files across jobs and Domain projects")]
    public async Task ProcessesMultipleJobs()
    {
        using var backend = new ScratchBackend();
        const string other = "Acme.Store.Catalog";
        const string otherDomain = $"Modules/Catalog/{other}.Domain";
        foreach (var file in Directory.EnumerateFiles(backend.FullPath(ScratchBackend.DomainDirectory), "*",
                     SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(backend.FullPath(ScratchBackend.DomainDirectory), file)
                .Replace(ScratchBackend.ModuleName, other, StringComparison.Ordinal);
            var content = File.ReadAllText(file).Replace(ScratchBackend.ModuleName, other, StringComparison.Ordinal);
            backend.Write($"{otherDomain}/{relative}", content);
        }

        var first = ScratchBackend.Job("CreateCommand");
        var models = first["models"]!.AsArray();
        var warehouse = models[0]!.DeepClone().AsObject();
        warehouse["aggregate"] = $"{ScratchBackend.ModuleName}.Domain.Warehouses.Warehouse";
        warehouse["route"] = "warehouses";
        warehouse["detailsFields"] = new JsonArray("Id", "Name");
        warehouse["listFields"] = new JsonArray("Id");
        warehouse.Remove("children");
        models.Add(warehouse);

        var second = ScratchBackend.Job("CreateCommand");
        second["pathToModels"] = $"{otherDomain}/{other}.Domain.csproj";
        second["models"]![0]!["aggregate"] = $"{other}.Domain.Products.Product";
        second["outputDirectory"] = new JsonObject { ["CreateCommand"] = "output/{ModuleName}/{Feature}/{Resource}" };

        var files = await backend.RunAsync(first, second);

        files.Count.ShouldBe(3);
        files.ShouldContain(file => Path.GetFileName(file.Path) == "CreateWarehouseCommand.cs");
        var catalogCommand = Path.Combine("output", "Catalog", "Products", "Product", "CreateProductCommand.cs");
        files.ShouldContain(file => file.Path == catalogCommand);
        files.ShouldNotContain(file => file.Path.Contains("Variants", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Override an artifact and a bundled partial with repository files")]
    public async Task OverridesTemplates()
    {
        using var backend = new ScratchBackend();
        var job = ScratchBackend.Job("CreateCommand", "UpdateCommand");
        job["templates"] = new JsonObject
        {
            ["CreateCommand"] = "Overrides/Create.sbn",
            ["DtoRecord.sbn"] = "Overrides/DtoRecord.sbn"
        };
        job["outputDirectory"] = new JsonObject { ["CreateCommand"] = "generated/{Resource}" };
        backend.Write("tools/Generator/Overrides/Create.sbn", "// Custom {{ model.resource }}\n");
        backend.Write("tools/Generator/Overrides/DtoRecord.sbn", "// Local partial for {{ dto_name }}\n");

        var files = await backend.RunAsync(job);

        var commandPath = Path.Combine("generated", "Product", "CreateProductCommand.cs");
        files.ShouldContain(new GeneratedFile(commandPath, GeneratedFileAction.Created));
        backend.Read(commandPath).ShouldContain("// Custom Product");
        var update = files.Single(file => Path.GetFileName(file.Path) == "UpdateProductCommand.cs");
        backend.Read(update.Path).ShouldContain("// Local partial for UpdateProductCommand");
    }

    [Theory(DisplayName = "Invalid artifact or template names fail before writing")]
    [InlineData("generatedFiles", "MissingArtifact", "Unknown generated file")]
    [InlineData("templates", "Missing.sbn", "Unknown template")]
    [InlineData("outputDirectory", "DtoRecord.sbn", "Unknown generated file")]
    public async Task RejectsUnknownNames(string setting, string name, string error)
    {
        using var backend = new ScratchBackend();
        var job = ScratchBackend.Job("CreateCommand");
        job[setting] = setting == "generatedFiles" ? new JsonArray(name) : new JsonObject { [name] = "override.sbn" };

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => backend.RunAsync(job));

        exception.Message.ShouldContain(error);
        Directory.EnumerateFiles(backend.Root, "CreateProductCommand.cs", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact(DisplayName = "Reject invalid Scriban without applying an earlier artifact")]
    public async Task RejectsBrokenTemplates()
    {
        using var backend = new ScratchBackend();
        var job = ScratchBackend.Job("CreateCommand", "UpdateCommand");
        job["templates"] = new JsonObject { ["UpdateCommand"] = "Broken.sbn" };
        backend.Write("tools/Generator/Broken.sbn", "{{ if }}");

        await Should.ThrowAsync<InvalidOperationException>(() => backend.RunAsync(job));

        Directory.EnumerateFiles(backend.Root, "CreateProductCommand.cs", SearchOption.AllDirectories).ShouldBeEmpty();
        Directory.EnumerateFiles(backend.Root, "UpdateProductCommand.cs", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact(DisplayName = "Reject a route shared by two aggregates before writing endpoints")]
    public async Task RejectsDuplicateRoutes()
    {
        using var backend = new ScratchBackend();
        var job = ScratchBackend.Job("Endpoints");
        var model = job["models"]![0]!.DeepClone().AsObject();
        model["aggregate"] = $"{ScratchBackend.ModuleName}.Domain.Warehouses.Warehouse";
        model["detailsFields"] = new JsonArray("Id", "Name");
        model["listFields"] = new JsonArray("Id");
        job["models"]!.AsArray().Add(model);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => backend.RunAsync(job));

        exception.Message.ShouldContain("route 'products'");
        Directory.EnumerateFiles(backend.Root, "*Endpoints.cs", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact(DisplayName = "Child artifacts require a configured entity collection")]
    public async Task RequiresChildConfiguration()
    {
        using var backend = new ScratchBackend();
        var job = ScratchBackend.Job("ChildCreateCommand");
        job["models"]![0]!.AsObject().Remove("children");

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => backend.RunAsync(job));

        exception.Message.ShouldContain("requires children");
    }

    [Fact(DisplayName = "Invalid domain method shapes are reported without emitting files")]
    public async Task RejectsInvalidChildMethod()
    {
        using var backend = new ScratchBackend();
        var job = ScratchBackend.Job("CreateCommand", "ChildCreateCommand");
        job["models"]![0]!["children"]![0]!["createMethod"] = "MissingMethod";

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => backend.RunAsync(job));

        exception.Message.ShouldContain("MissingMethod");
        Directory.EnumerateFiles(backend.Root, "CreateProductCommand.cs", SearchOption.AllDirectories).ShouldBeEmpty();
    }
}
