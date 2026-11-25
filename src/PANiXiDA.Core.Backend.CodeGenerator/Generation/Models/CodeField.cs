namespace PANiXiDA.Core.Backend.CodeGenerator.Generation.Models;

internal sealed record CodeField(
    string Name,
    string Type,
    bool Optional,
    string? ValueObject,
    string? ValueMember,
    bool HasMaxLength,
    IReadOnlyList<ValueComponent>? Components = null,
    bool NullableStruct = false)
{
    public bool Composite => Components is not null;

    public string LocalName => char.ToLowerInvariant(Name[0]) + Name[1..];

    public bool RequiredString => Type == "string";

    public bool HasValidation => Composite ? !Optional : RequiredString || ValueObject is not null;
}
