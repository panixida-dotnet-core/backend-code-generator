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
    private static readonly HashSet<string> s_reservedKeywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint",
        "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    ];

    public string AggregateFeature => Child?.ParentFeature ?? Feature;

    public string AggregateDomain => Child?.ParentDomain ?? Domain;

    public string AggregateResource => Child?.ParentResource ?? Resource;

    public string ReadFeature => AggregateFeature;

    public string DetailsParameter
    {
        get
        {
            var name = char.ToLowerInvariant(Resource[0]) + Resource[1..];
            return s_reservedKeywords.Contains(name) ? name + "ReadModel" : name;
        }
    }

    public string WriteRepositoryParameter =>
        char.ToLowerInvariant(AggregateFeature[0]) + AggregateFeature[1..] + "Repository";

    public bool RequiresWriteAggregateAlias =>
        WriteAggregateNaming.RequiresAggregateAlias(Module, AggregateFeature, AggregateResource)
        || Child?.HasConflictingDomainImports == true;

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
