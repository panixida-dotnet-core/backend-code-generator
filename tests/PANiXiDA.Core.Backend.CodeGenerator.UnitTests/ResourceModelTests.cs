using PANiXiDA.Core.Backend.CodeGenerator.Configuration;
using PANiXiDA.Core.Backend.CodeGenerator.Generation.Domain;
using PANiXiDA.Core.Backend.CodeGenerator.Generation.Models;
using PANiXiDA.Core.Backend.CodeGenerator.UnitTests.Domain.Models;

namespace PANiXiDA.Core.Backend.CodeGenerator.UnitTests;

public sealed class ResourceModelTests
{
    private static readonly DomainProject s_project = new("Samples.Catalog.Domain.csproj",
        "PANiXiDA.Core.Backend.CodeGenerator.UnitTests", "UnitTests");

    [Fact(DisplayName = "Ignore identity, navigation properties, indexers, and properties without two accessors")]
    public void ReadsOnlyWritableScalarFields()
    {
        var target = Target(nameof(ValidRoot));

        var model = ResourceModelFactory.Create(typeof(ValidRoot).Assembly, target, Settings(target));

        model.Fields.Select(field => field.Name).ShouldBe([nameof(ValidRoot.Count)]);
    }

    [Theory(DisplayName = "Reject unsupported aggregate types and ID shapes")]
    [InlineData("MissingRoot", "public concrete aggregate")]
    [InlineData(nameof(InternalRoot), "public concrete aggregate")]
    [InlineData(nameof(StructRoot), "public concrete aggregate")]
    [InlineData(nameof(AbstractRoot), "public concrete aggregate")]
    [InlineData("GenericRoot`1", "public concrete aggregate")]
    [InlineData(nameof(UnrelatedRoot), "derive from AggregateRoot")]
    [InlineData(nameof(WrongBaseRoot), "derive from AggregateRoot")]
    [InlineData(nameof(ForeignIdRoot), "typed Guid ID")]
    [InlineData(nameof(WrongValueRoot), "typed Guid ID")]
    [InlineData(nameof(MissingValueRoot), "typed Guid ID")]
    [InlineData(nameof(MissingFactoryRoot), "typed Guid ID")]
    [InlineData(nameof(MissingUpdateRoot), "expected Result type")]
    public void RejectsInvalidAggregate(string type, string message)
    {
        var target = Target(type);

        var error = Should.Throw<InvalidOperationException>(() =>
            ResourceModelFactory.Create(typeof(ValidRoot).Assembly, target, Settings(target)));

        error.Message.ShouldContain(message);
    }

    [Fact(DisplayName = "Read projections do not require unused write operations")]
    public void ReadsAggregateWithoutUpdateOperation()
    {
        var target = Target(nameof(MissingUpdateRoot));

        var model = ResourceModelFactory.Create(
            typeof(ValidRoot).Assembly, target, Settings(target), includeCreate: false, includeUpdate: false);

        model.CreateFields.ShouldBeEmpty();
        model.UpdateFields.ShouldBeEmpty();
        model.DetailsFields.Select(field => field.Name).ShouldContain("Id");
    }

    [Theory(DisplayName = "Require a lowercase segment for an aggregate route")]
    [InlineData(null)]
    [InlineData("Products/Children")]
    public void RejectsInvalidRootRoutes(string? route)
    {
        var target = Target(nameof(ValidRoot));

        var error = Should.Throw<InvalidOperationException>(() =>
            ResourceModelFactory.Create(typeof(ValidRoot).Assembly, target, Settings(target) with { Route = route! }));

        error.Message.ShouldContain("lowercase, single-segment");
    }

    [Theory(DisplayName = "Reject invalid child collections and write contracts")]
    [InlineData("Missing", "roots", "variants", "AddVariant", "public collection")]
    [InlineData(nameof(ValidRoot.WriteOnlyVariants), "roots", "variants", "AddVariant", "public collection")]
    [InlineData(nameof(ValidRoot.Scalars), "roots", "variants", "AddVariant", "public collection")]
    [InlineData(nameof(ValidRoot.Hidden), "roots", "variants", "AddVariant", "public collection")]
    [InlineData(nameof(ValidRoot.StructChildren), "roots", "variants", "AddVariant", "public collection")]
    [InlineData(nameof(ValidRoot.AbstractChildren), "roots", "variants", "AddVariant", "public collection")]
    [InlineData(nameof(ValidRoot.GenericChildren), "roots", "variants", "AddVariant", "public collection")]
    [InlineData(nameof(ValidRoot.AggregateChildren), "roots", "variants", "AddVariant", "derive from Entity")]
    [InlineData(nameof(ValidRoot.ReservedChildren), "roots", "variants", "AddVariant", "reserved for the generated association")]
    [InlineData(nameof(ValidRoot.Variants), "Roots", "variants", "AddVariant", "lowercase route segments")]
    [InlineData(nameof(ValidRoot.Variants), "roots", "Variants", "AddVariant", "lowercase route segments")]
    [InlineData(nameof(ValidRoot.Variants), "roots", "variants", null, "requires a method")]
    public void RejectsInvalidChildren(string collection, string rootRoute, string childRoute, string? method, string message)
    {
        var parent = Target(nameof(ValidRoot));
        var settings = new ChildSettings(collection, "Child", childRoute, ["Id"], ["Id"], method, "UpdateVariant", "RemoveVariant");

        var error = Should.Throw<InvalidOperationException>(() => ResourceModelFactory.CreateChild(
            typeof(ValidRoot).Assembly, parent, parent.ForChild(settings), rootRoute, settings, ["ChildCreateHandler"]));

        error.Message.ShouldContain(message);
    }

    [Fact(DisplayName = "Describe a child query without requiring write methods")]
    public void SupportsReadOnlyChildren()
    {
        var parent = Target(nameof(ValidRoot));
        var settings = new ChildSettings(nameof(ValidRoot.Variants), "Variant", "variants", ["Id"], ["Id"]);

        var child = ResourceModelFactory.CreateChild(typeof(ValidRoot).Assembly, parent, parent.ForChild(settings),
            "roots", settings, ["ChildGetListHandler"]);

        child.Route.ShouldBe("roots/{validrootid:guid}/variants");
        child.CreateFields.ShouldBeEmpty();
        child.UpdateFields.ShouldBeEmpty();
    }

    private static ResourceTarget Target(string type) => new(s_project, "Models", type);

    private static ResourceSettings Settings(ResourceTarget target) => new(target.Aggregate, "roots", ["Id"], ["Id"], null);
}
