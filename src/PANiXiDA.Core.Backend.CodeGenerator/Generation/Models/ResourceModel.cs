using PANiXiDA.Core.Backend.CodeGenerator.Generation.Domain;

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
    private static readonly string[] s_writeLocals =
        ["command", "cancellationToken", "idResult", "parentIdResult", "validation", "updateResult", "item"];

    private static readonly string[] s_readLocals = ["query", "cancellationToken"];

    public string AggregateFeature => Child?.ParentFeature ?? Feature;

    public string AggregateDomain => Child?.ParentDomain ?? Domain;

    public string AggregateResource => Child?.ParentResource ?? Resource;

    public string ReadFeature => AggregateFeature;

    public string ReadRepositoryParameter =>
        char.ToLowerInvariant(ReadFeature[0]) + ReadFeature[1..] + "ReadRepository";

    public string WriteRepositoryParameter =>
        char.ToLowerInvariant(AggregateFeature[0]) + AggregateFeature[1..] + "Repository";

    public string AggregateVariable => GetVariable(AggregateResource, "Aggregate", ReservedWriteNames);
    public string CreateResultVariable => GetVariable(
        Resource + (Child is null ? "Result" : "IdResult"), "Created", ReservedWriteNames.Append(AggregateVariable));

    public string DetailsVariable => GetVariable(Resource, "ReadModel", s_readLocals);
    public string ListVariable => GetVariable(Feature, "Page", s_readLocals);

    public bool RequiresWriteAggregateAlias =>
        WriteAggregateNaming.RequiresAggregateAlias(Module, AggregateFeature, AggregateResource)
        || Child?.HasConflictingDomainImports == true;

    public string ReadDbResource => Child?.ParentResource ?? Resource;

    public bool HasDetailsReadModel { get; init; }

    public bool HasListItemReadModel { get; init; }
    public bool HasDetailsEndpoint { get; init; }

    public bool HasFilter { get; init; }

    public bool HasFilterValidator { get; init; }

    public WriteHandlerTypes? CreateHandlerTypes { get; init; }

    public WriteHandlerTypes? UpdateHandlerTypes { get; init; }

    public IReadOnlyList<ResourceModel> ChildReadContracts { get; init; } = [];

    public CodeField[] SearchFields => [.. ListFields.Where(item => item.Type is "string" or "string?")];

    public bool HasSearchFilter => HasFilter && SearchFields.Length > 0;

    public CodeField[] CreateValidationFields => [.. CreateFields.Where(item => item.HasValidation)];

    public CodeField[] UpdateValidationFields => [.. UpdateFields.Where(item => item.HasValidation)];

    public bool HasCompoundFields => Fields.Any(item => item.Composite);

    public bool HasCompoundDetails => DetailsFields.Any(item => item.Composite);

    public bool HasCompoundList => ListFields.Any(item => item.Composite);

    private IEnumerable<string> ReservedWriteNames =>
        s_writeLocals
            .Concat(CreateFields.Concat(UpdateFields)
                .Where(item => item.BindsDomainValue)
                .SelectMany(item => new[] { item.LocalName + "Input", item.LocalName + "Result" }))
            .Concat(CreateHandlerTypes!.DomainTypes.Values)
            .Concat(UpdateHandlerTypes!.DomainTypes.Values);

    private static string GetVariable(string domainName, string suffix, IEnumerable<string> reserved)
    {
        var name = char.ToLowerInvariant(domainName[0]) + domainName[1..];
        var candidate = name;
        var index = 1;
        while (reserved.Contains(candidate, StringComparer.Ordinal))
        {
            candidate = $"{name}{suffix}{index}";
            index++;
        }

        return CSharpIdentifier.Escape(candidate, isAsync: true);
    }
}
