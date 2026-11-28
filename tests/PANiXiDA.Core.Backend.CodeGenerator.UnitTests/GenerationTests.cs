using System.Text.Json.Nodes;

namespace PANiXiDA.Core.Backend.CodeGenerator.UnitTests;

public sealed class GenerationTests
{
    [Fact(DisplayName = "Generate and compile all aggregate and child CRUD files using only embedded templates")]
    public async Task GeneratesFullCrud()
    {
        using var backend = new ScratchBackend();

        var files = await backend.RunAsync(ScratchBackend.Job());

        files.Count.ShouldBe(87);
        files.Select(file => file.Path).Distinct().Count().ShouldBe(files.Count);
        files.ShouldAllBe(file => file.Action == GeneratedFileAction.Created);
        var code = files.Select(file => backend.Read(file.Path)).ToArray();
        code.ShouldContain(source => source.Contains("DomainAggregate.Create(", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("var nameResult =", StringComparison.Ordinal)
            && source.Contains("ProductName.Create(", StringComparison.Ordinal)
            && source.Contains("ProductKind.Create(", StringComparison.Ordinal)
            && source.Contains("ProductDescription.Create(", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("ProductSku.Create(", StringComparison.Ordinal)
            && source.Contains("command.Sku", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("ProductKind.Create", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("Money.Create(", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("var listPriceResult = command.ListPrice is { } listPriceInput", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("@listPrice: listPriceResult?.Value", StringComparison.Ordinal));
        foreach (var operation in new[] { "Create", "Update" })
        {
            var aggregateHandler = files.Single(file => Path.GetFileName(file.Path) == $"{operation}ProductHandler.cs");
            var childHandler = files.Single(file => Path.GetFileName(file.Path) == $"{operation}ProductVariantHandler.cs");
            var aggregateSource = backend.Read(aggregateHandler.Path).ReplaceLineEndings("\n");
            aggregateSource.ShouldContain($"using DomainAggregate = {ScratchBackend.Product};");
            aggregateSource.ShouldContain($"using {ScratchBackend.ModuleName}.Domain.Products.ValueObjects;");
            aggregateSource.ShouldContain("var nameResult = ProductName.Create(command.Name);");
            aggregateSource.ShouldNotContain("global::");
            aggregateSource.ShouldContain(";\n\nnamespace ");
            aggregateSource.ShouldContain(
                "Result.Combine(\n            nameResult,\n            kindResult,\n            descriptionResult ?? Result.Success(),\n"
                + "            listPriceResult ?? Result.Success());");
            var childSource = backend.Read(childHandler.Path).ReplaceLineEndings("\n");
            childSource.ShouldContain($"using {ScratchBackend.ModuleName}.Domain.Products.ValueObjects;");
            childSource.ShouldContain("var parentIdResult = ProductId.Create(command.ParentId);");
            childSource.ShouldContain("var skuResult = ProductSku.Create(command.Sku);");
            childSource.ShouldNotContain("global::");
            childSource.ShouldContain(";\n\nnamespace ");
            childSource.ShouldNotContain("using DomainAggregate");
            childSource.ShouldContain("Result.Combine(\n            skuResult,\n            priceResult,\n            stockResult);");
            childSource.ShouldContain("Error.Validation(\"Price is required.\")");
            childSource.ShouldNotContain("if (priceResult.IsFailure)");
        }
        code.ShouldContain(source => source.Contains("parent.AddVariant(", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("parent.UpdateVariant(", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("parent.RemoveVariant(", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("ProductVariantDetailsReadModelMapper.ProjectTo(query)", StringComparison.Ordinal)
            && source.Contains(".FirstOrDefaultAsync(item => item.Id == id, cancellationToken)", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("GetVariantsPageAsync(", StringComparison.Ordinal)
            && source.Contains("Query.Where(parent => parent.Id == parentId)", StringComparison.Ordinal)
            && source.Contains("GetPagedResultAsync<", StringComparison.Ordinal)
            && source.Contains("ProductVariantListItemReadModelSorting>", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains(
            "IReadModelMapper<Guid, ProductReadDbModel, ProductVariantListItemReadModel>", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains(
            "IReadModelMapper<Guid, ProductReadDbModel, ProductVariantDetailsReadModel>", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("public sealed record ProductVariantListItemReadModel : IReadModel", StringComparison.Ordinal)
            && source.Contains("public Guid Id { get; init; }", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("[Table(\"product_variants\")]", StringComparison.Ordinal)
            && source.Contains("public List<ProductVariantReadDbModel> Variants", StringComparison.Ordinal));
        files.ShouldContain(file => file.Path.EndsWith(
            Path.Combine("Products", "Common", "Filters", "ProductsFilter.cs"), StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("public sealed record ProductsFilter(string? Search = null) : IFilter;", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("public sealed class ProductsFilterValidator : AbstractValidator<ProductsFilter>;", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains(".SetValidator(new ProductsFilterValidator())", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("record GetProductsRequest([FromQuery(Name = \"search\")] string? Search = null);",
            StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("record GetProductVariantsRequest([FromQuery(Name = \"search\")] string? Search = null);",
            StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("[AsParameters] GetProductsRequest request,", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("[AsParameters] GetProductVariantsRequest request,", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("[MapPropertyFromSource(nameof(GetProductsListQuery.Filter))]",
            StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("[MapPropertyFromSource(nameof(GetProductVariantsListQuery.Filter))]",
            StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("filter: query.Filter,", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("ApplyFilter(Query, filter)", StringComparison.Ordinal)
            && source.Contains("matchExpression: item.Name", StringComparison.Ordinal)
            && source.Contains("pattern:", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("query = ApplyFilter(query, filter);", StringComparison.Ordinal)
            && source.Contains("Variants = parent.Variants", StringComparison.Ordinal)
            && source.Contains("matchExpression: item.Sku", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("productsReadRepository.GetVariantsItemByIdAsync(", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("productsReadRepository.GetVariantsPageAsync(", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("products/{productid:guid}/variants", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("OwnsMany(item => item.Variants", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("IEntityTypeConfiguration<DomainAggregate>", StringComparison.Ordinal)
            && source.Contains($"using DomainAggregate = {ScratchBackend.Product};", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains(".RequireAuthorization();", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("DeleteProductMapper.ToCommand(id: id)", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("partial DeleteProductCommand ToCommand(Guid id);", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("partial DeleteProductVariantCommand ToCommand(Guid parentId, Guid id);",
            StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("partial GetProductVariantDetailsQuery ToQuery(Guid parentId, Guid id);",
            StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("ProductDetailsReadModel readModel);", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("ProductVariantDetailsReadModel readModel);", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("ProductDetailsMapper.ToResponse(readModel: readModel)", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("ProductVariantDetailsMapper.ToResponse(readModel: readModel)",
            StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("routeName: new GetProductDetailsEndpoint().Name", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains(".SetValidator(new ProductVariantListItemReadModelSortingValidator())", StringComparison.Ordinal));
        code.ShouldNotContain(source => source.Contains("RuleForEach(query => query.SortingParameters.Fields)", StringComparison.Ordinal));
        code.ShouldContain(source => source.Contains("HasColumnType(\"jsonb\")", StringComparison.Ordinal));
        Directory.EnumerateFiles(backend.Root, "*.sbn", SearchOption.AllDirectories).ShouldBeEmpty();

        await backend.BuildLayerAsync("Infrastructure");
        await backend.BuildLayerAsync("Presentation");
    }

    [Fact(DisplayName = "Use the short aggregate name in write code unless the surrounding namespace or imports collide")]
    public async Task UsesWriteAggregateAliasOnlyForNameCollisions()
    {
        using var backend = new ScratchBackend();
        var job = ScratchBackend.Job("IRepository", "Repository", "Configuration",
            "CreateCommand", "CreateHandler", "UpdateCommand", "UpdateHandler");
        var models = job["models"]!.AsArray();
        models.Clear();
        (string Feature, string Aggregate, bool UsePersistenceAlias, bool UseHandlerAlias)[] cases =
        [
            ("Warehouses", "Warehouse", false, false),
            ("Contexts", "DbContext", false, false),
            ("Modules", "Module", true, true),
            ("Files", "File", true, true),
            ("Inventory", "Inventory", true, true),
            ("Persistences", "Persistence", true, false)
        ];
        foreach (var (feature, aggregate, _, _) in cases)
        {
            if (aggregate != "Warehouse")
            {
                foreach (var suffix in new[] { ".cs", "Id.cs" })
                {
                    var source = backend.Read($"{ScratchBackend.DomainDirectory}/Warehouses/Warehouse{suffix}")
                        .Replace("Warehouses", feature, StringComparison.Ordinal)
                        .Replace("Warehouse", aggregate, StringComparison.Ordinal);
                    backend.Write($"{ScratchBackend.DomainDirectory}/{feature}/{aggregate}{suffix}", source);
                }
            }

            models.Add(new JsonObject
            {
                ["aggregate"] = $"{ScratchBackend.ModuleName}.Domain.{feature}.{aggregate}",
                ["route"] = feature.ToLowerInvariant(),
                ["detailsFields"] = new JsonArray("Id"),
                ["listFields"] = new JsonArray("Id")
            });
        }

        var files = await backend.RunAsync(job);

        foreach (var (feature, aggregate, usePersistenceAlias, useHandlerAlias) in cases)
        {
            var repository = files.Single(file => Path.GetFileName(file.Path) == $"{feature}Repository.cs");
            var configuration = files.Single(file => Path.GetFileName(file.Path) == $"{aggregate}Configuration.cs");
            var createHandler = files.Single(file => Path.GetFileName(file.Path) == $"Create{aggregate}Handler.cs");
            var updateHandler = files.Single(file => Path.GetFileName(file.Path) == $"Update{aggregate}Handler.cs");
            var source = backend.Read(repository.Path);
            var configurationSource = backend.Read(configuration.Path);
            var createSource = backend.Read(createHandler.Path);
            var updateSource = backend.Read(updateHandler.Path);
            createSource.ShouldNotContain("global::");
            updateSource.ShouldNotContain("global::");
            if (usePersistenceAlias)
            {
                source.ShouldContain($"using DomainAggregate = {ScratchBackend.ModuleName}.Domain.{feature}.{aggregate};");
                source.ShouldContain($"EfRepository<ModuleWriteDbContext, {aggregate}Id, DomainAggregate>");
                configurationSource.ShouldContain($"using DomainAggregate = {ScratchBackend.ModuleName}.Domain.{feature}.{aggregate};");
                configurationSource.ShouldContain("AuditableEntityConfiguration<DomainAggregate>");
            }
            else
            {
                source.ShouldNotContain("using DomainAggregate");
                source.ShouldContain($"EfRepository<ModuleWriteDbContext, {aggregate}Id, {aggregate}>");
                configurationSource.ShouldNotContain("using DomainAggregate");
                configurationSource.ShouldContain($"AuditableEntityConfiguration<{aggregate}>");
            }

            if (useHandlerAlias)
            {
                createSource.ShouldContain($"using DomainAggregate = {ScratchBackend.ModuleName}.Domain.{feature}.{aggregate};");
                createSource.ShouldContain("DomainAggregate.Create(");
                updateSource.ShouldContain(".WithField(typeof(DomainAggregate).Name)");
            }
            else
            {
                createSource.ShouldNotContain("using DomainAggregate");
                createSource.ShouldContain($"{aggregate}.Create(");
                updateSource.ShouldContain($".WithField(nameof({aggregate}))");
            }
        }

        await backend.BuildLayerAsync("Infrastructure");
    }

    [Fact(DisplayName = "Compile write handlers with aliases for duplicate value object and child identifier names")]
    public async Task AliasesConflictingWriteHandlerTypes()
    {
        using var backend = new ScratchBackend();
        backend.Write($"{ScratchBackend.DomainDirectory}/Warehouses/Warehouse.cs", """
            namespace Organization.Product.Module.Domain.Warehouses;

            public sealed class Warehouse : AggregateRoot<WarehouseId>
            {
                private Warehouse(WarehouseId id) : base(id) { }

                public Collision.First.Amount First { get; private set; } = default!;
                public Collision.Second.Amount Second { get; private set; } = default!;
                public Collision.Threading.Counter Count { get; private set; } = default!;
                public Measure? Measure { get; private set; }

                public static Result<Warehouse> Create(
                    Collision.First.Amount first,
                    Collision.Second.Amount second,
                    Collision.Threading.Counter count,
                    Measure? measure) =>
                    Result.Success(new Warehouse(WarehouseId.New())
                        { First = first, Second = second, Count = count, Measure = measure });
            }
            """);
        backend.Write($"{ScratchBackend.DomainDirectory}/Warehouses/Measure.cs", """
            public readonly record struct Measure(int Value)
            {
                public static Result<Measure?> Create(int? value) =>
                    Result.Success<Measure?>(value is { } number ? new Measure(number) : null);
            }
            """);
        foreach (var (nameSpace, name) in new[]
                 {
                     ("Collision.First", "Amount"), ("Collision.Second", "Amount"), ("Collision.Threading", "Counter")
                 })
        {
            backend.Write($"{ScratchBackend.DomainDirectory}/Warehouses/{nameSpace}.{name}.cs", $$"""
                namespace {{nameSpace}};

                public sealed record {{name}}(int Value)
                {
                    public static Result<{{name}}> Create(int value) => Result.Success(new {{name}}(value));
                }
                """);
        }

        backend.Write($"{ScratchBackend.DomainDirectory}/Warehouses/Task.cs", "namespace Collision.Threading; public sealed class Task;");
        backend.Write($"{ScratchBackend.DomainDirectory}/Warehouses/Error.cs", "namespace Collision.First; internal sealed class Error;");
        const string childNamespace = ScratchBackend.ModuleName + ".Domain.Products.Entities.ProductVariants";
        const string valueObjects = ScratchBackend.ModuleName + ".Domain.Products.ValueObjects";
        backend.Write($"{ScratchBackend.DomainDirectory}/Products/ValueObjects/ProductVariantId.cs",
            $"namespace {valueObjects}; public sealed class ProductVariantId;");
        var productPath = $"{ScratchBackend.DomainDirectory}/Products/Product.cs";
        backend.Write(productPath, backend.Read(productPath)
            .Replace("ProductVariantId", $"global::{childNamespace}.ProductVariantId", StringComparison.Ordinal));

        var warehouseJob = ScratchBackend.Job("IRepository", "CreateCommand", "CreateHandler");
        var warehouse = warehouseJob["models"]![0]!.AsObject();
        warehouse["aggregate"] = $"{ScratchBackend.ModuleName}.Domain.Warehouses.Warehouse";
        warehouse["route"] = "warehouses";
        warehouse["detailsFields"] = new JsonArray("Id");
        warehouse["listFields"] = new JsonArray("Id");
        var productJob = ScratchBackend.Job("IRepository", "ChildUpdateCommand", "ChildUpdateHandler");

        var files = await backend.RunAsync(warehouseJob, productJob);
        var create = backend.Read(files.Single(file => Path.GetFileName(file.Path) == "CreateWarehouseHandler.cs").Path);
        create.ShouldContain("using DomainAmount = Collision.First.Amount;");
        create.ShouldContain("using DomainAmount2 = Collision.Second.Amount;");
        create.ShouldContain("using DomainCounter = Collision.Threading.Counter;");
        create.ShouldNotContain("using Collision.Threading;");
        create.ShouldContain("Measure.Create(measureInput)");
        var update = backend.Read(files.Single(file => Path.GetFileName(file.Path) == "UpdateProductVariantHandler.cs").Path);
        update.ShouldContain($"using DomainProductVariantId = {childNamespace}.ProductVariantId;");
        update.ShouldContain("DomainProductVariantId.Create(command.Id)");

        await backend.BuildLayerAsync("Application");
    }

    [Theory(DisplayName = "Choose the aggregate reference for EF child configuration from both domain namespaces")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UsesParentAggregateTypeInChildConfiguration(bool hasConflictingType)
    {
        using var backend = new ScratchBackend();
        const string warehouse = ScratchBackend.DomainDirectory + "/Warehouses/Warehouse.cs";
        const string childDomain = ScratchBackend.ModuleName + ".Domain.Products.Entities.ProductVariants";
        const string parentDomain = ScratchBackend.ModuleName + ".Domain.Warehouses";
        var source = backend.Read(warehouse).Replace(
            "    public string Name",
            "    public IReadOnlyCollection<ProductVariant> Variants { get; } = [];\n\n    public string Name",
            StringComparison.Ordinal);
        backend.Write(warehouse, $"using {childDomain};\n\n{source}");
        if (hasConflictingType)
        {
            backend.Write(
                $"{ScratchBackend.DomainDirectory}/Warehouses/ProductVariantId.cs",
                $"namespace {parentDomain};\n\npublic sealed class ProductVariantId;\n");
        }

        var job = ScratchBackend.Job("ChildConfiguration");
        var model = job["models"]![0]!.AsObject();
        model["aggregate"] = $"{parentDomain}.Warehouse";
        model["route"] = "warehouses";
        model["detailsFields"] = new JsonArray("Id");
        model["listFields"] = new JsonArray("Id");

        var files = await backend.RunAsync(job);

        var configuration = backend.Read(files.Single().Path);
        if (hasConflictingType)
        {
            configuration.ShouldContain($"using DomainAggregate = {parentDomain}.Warehouse;");
            configuration.ShouldContain("OwnedNavigationBuilder<DomainAggregate, ProductVariant>");
        }
        else
        {
            configuration.ShouldNotContain("using DomainAggregate");
            configuration.ShouldContain($"using {parentDomain};");
            configuration.ShouldContain("OwnedNavigationBuilder<Warehouse, ProductVariant>");
        }

        await backend.BuildLayerAsync("Infrastructure");
    }

    [Fact(DisplayName = "Generate a create endpoint without a details endpoint")]
    public async Task GeneratesCreateEndpointWithoutDetails()
    {
        using var backend = new ScratchBackend();
        var files = await backend.RunAsync(ScratchBackend.Job(
            "Endpoints", "CreateCommand", "CreateEndpoint", "CreateRequest", "CreateResponse", "CreateMapper"));

        var endpoint = files.Single(file => Path.GetFileName(file.Path) == "CreateProductEndpoint.cs");
        var source = backend.Read(endpoint.Path);
        source.ShouldContain("TypedResults.Created(");
        source.ShouldNotContain("GetProductDetailsEndpoint");
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

        var first = ScratchBackend.Job("CreateCommand", "CreateHandler");
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

        files.Count.ShouldBe(5);
        files.ShouldContain(file => Path.GetFileName(file.Path) == "CreateWarehouseCommand.cs");
        var warehouseHandler = files.Single(file => Path.GetFileName(file.Path) == "CreateWarehouseHandler.cs");
        var warehouseSource = backend.Read(warehouseHandler.Path);
        warehouseSource.ShouldContain("@name: command.Name");
        warehouseSource.ShouldNotContain("Result.Combine(");
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
