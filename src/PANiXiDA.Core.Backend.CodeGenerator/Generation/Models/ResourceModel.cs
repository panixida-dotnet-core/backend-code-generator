namespace PANiXiDA.Core.Backend.CodeGenerator.Generation.Models;

internal sealed record ResourceModel(
    string Module,
    string ModuleName,
    string Feature,
    string Resource,
    string Domain,
    string Id,
    string Route,
    IReadOnlyList<CodeField> Fields,
    IReadOnlyList<CodeField> CreateFields,
    IReadOnlyList<CodeField> UpdateFields,
    IReadOnlyList<CodeField> DetailsFields,
    IReadOnlyList<CodeField> ListFields,
    ChildModel? Child = null)
{
    public string AggregateFeature => Child?.ParentFeature ?? Feature;

    public string ReadFeature => AggregateFeature;

    public string WriteRepositoryParameter =>
        char.ToLowerInvariant(AggregateFeature[0]) + AggregateFeature[1..] + "Repository";

    public bool RequiresWriteRepositoryAggregateAlias =>
        WriteRepositoryNaming.RequiresAggregateAlias(Module, Feature, Resource);

    public string ReadDbResource => Child?.ParentResource ?? Resource;

    public bool HasDetailsReadModel { get; init; }

    public bool HasListItemReadModel { get; init; }
    public bool HasDetailsEndpoint { get; init; }

    public bool HasFilter { get; init; }

    public bool HasFilterValidator { get; init; }

    public IReadOnlyList<ResourceModel> ChildReadContracts { get; init; } = [];

    public CodeField[] SearchFields => [.. ListFields.Where(item => item.Type is "string" or "string?")];

    public bool HasSearchFilter => HasFilter && SearchFields.Length > 0;

    public CodeField[] CreateValidationFields => [.. CreateFields.Where(item => item.HasValidation)];

    public CodeField[] UpdateValidationFields => [.. UpdateFields.Where(item => item.HasValidation)];

    public bool HasCompoundFields => Fields.Any(item => item.Composite);

    public bool HasCompoundDetails => DetailsFields.Any(item => item.Composite);

    public bool HasCompoundList => ListFields.Any(item => item.Composite);
}
