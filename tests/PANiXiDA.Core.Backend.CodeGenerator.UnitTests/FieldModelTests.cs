using System.Reflection;

using PANiXiDA.Core.Backend.CodeGenerator.Generation.Models;

namespace PANiXiDA.Core.Backend.CodeGenerator.UnitTests;

public sealed class FieldModelTests
{
    [Theory(DisplayName = "Reject unsupported fields and compound factories with unmatched components")]
    [InlineData(nameof(FieldSamples.PrivateValue))]
    [InlineData(nameof(FieldSamples.GenericValue))]
    [InlineData(nameof(FieldSamples.WriteOnlyValue))]
    [InlineData(nameof(FieldSamples.PrivateGetterValue))]
    [InlineData(nameof(FieldSamples.StaticValue))]
    [InlineData(nameof(FieldSamples.NonScalarValue))]
    [InlineData(nameof(FieldSamples.MissingFactory))]
    [InlineData(nameof(FieldSamples.WrongFactory))]
    [InlineData(nameof(FieldSamples.WrongGenericFactory))]
    [InlineData(nameof(FieldSamples.WrongPayload))]
    [InlineData(nameof(FieldSamples.RequiredWrongPayload))]
    [InlineData(nameof(FieldSamples.UnboundComponent))]
    [InlineData(nameof(FieldSamples.WrongComponentType))]
    [InlineData(nameof(FieldSamples.WrongComponentNullability))]
    [InlineData(nameof(FieldSamples.UnsupportedComponent))]
    [InlineData(nameof(FieldSamples.DuplicateComponents))]
    public void RejectsUnsupportedFields(string field)
    {
        var exception = Should.Throw<InvalidOperationException>(() => Describe(field));

        exception.Message.ShouldContain("with a matching Create factory");
    }

    [Fact(DisplayName = "Reject compound factories with ambiguous matching overloads")]
    public void RejectsAmbiguousFactory()
    {
        var exception = Should.Throw<InvalidOperationException>(() => Describe(nameof(FieldSamples.AmbiguousFactory)));

        exception.Message.ShouldContain("more than one matching compound Create factory");
    }

    [Fact(DisplayName = "Infer nullable scalar and value types and ignore unusable length metadata")]
    public void DescribesNullableFields()
    {
        var quantity = Describe(nameof(FieldSamples.NullableQuantity));
        var compound = Describe(nameof(FieldSamples.CompoundStruct));
        var reference = Describe(nameof(FieldSamples.NonLiteralLength));

        Describe(nameof(FieldSamples.NullableCount)).Type.ShouldBe("int?");
        quantity.Type.ShouldBe("int?");
        quantity.ValueMember.ShouldBe("Value.Value");
        compound.NullableStruct.ShouldBeTrue();
        compound.Components.ShouldBe([new ValueComponent("Count", "int?", "count"), new ValueComponent("Text", "string?", "text")]);
        reference.Type.ShouldBe("string?");
        reference.HasMaxLength.ShouldBeFalse();
        Describe(nameof(FieldSamples.NonIntegerLength)).HasMaxLength.ShouldBeFalse();
    }

    [Theory(DisplayName = "Require method inputs to match distinct writable properties and their nullability")]
    [InlineData(nameof(FieldSamples.WrongName), nameof(FieldSamples.Count))]
    [InlineData(nameof(FieldSamples.WrongScalarType), nameof(FieldSamples.Count))]
    [InlineData(nameof(FieldSamples.WrongScalarNullability), nameof(FieldSamples.Count))]
    [InlineData(nameof(FieldSamples.DuplicateBinding), nameof(FieldSamples.Count))]
    [InlineData(nameof(FieldSamples.WrongCompoundType), nameof(FieldSamples.CompoundStruct))]
    [InlineData(nameof(FieldSamples.WrongCompoundNullability), nameof(FieldSamples.CompoundStruct))]
    public void RejectsIncompatibleMethodInputs(string method, string property)
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            CodeFieldFactory.BindParameters(typeof(FieldSamples).GetMethod(method)!, [Describe(property)], new()));

        exception.Message.ShouldContain("match a writable domain property");
    }

    [Theory(DisplayName = "Require the typed child ID as the leading argument")]
    [InlineData(nameof(FieldSamples.NoParameters))]
    [InlineData(nameof(FieldSamples.WrongLeadingId))]
    public void RequiresLeadingId(string method)
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            CodeFieldFactory.BindParameters(typeof(FieldSamples).GetMethod(method)!, [], new(), typeof(Guid)));

        exception.Message.ShouldContain("must take 'Guid' first");
    }

    [Theory(DisplayName = "Require Id and unique, known read projection fields")]
    [InlineData(null, "contain Id")]
    [InlineData(new[] { "Count" }, "contain Id")]
    [InlineData(new[] { "Id", "Id" }, "distinct property names")]
    [InlineData(new[] { "Id", "Missing" }, "Unknown property 'Missing'")]
    public void ValidatesProjection(string[]? names, string message)
    {
        var fields = new[] { new CodeField("Id", "Guid", false, null, null, false), Describe(nameof(FieldSamples.Count)) };

        var exception = Should.Throw<InvalidOperationException>(() =>
            CodeFieldFactory.SelectProjection(names, fields, "detailsFields"));

        exception.Message.ShouldContain(message);
    }

    private static CodeField Describe(string name) =>
        CodeFieldFactory.DescribeField(typeof(FieldSamples).GetProperty(name)!, new NullabilityInfoContext());
}
