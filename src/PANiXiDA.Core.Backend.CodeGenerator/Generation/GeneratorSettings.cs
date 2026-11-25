namespace PANiXiDA.Core.Backend.CodeGenerator.Generation;

internal sealed record GeneratorSettings(IReadOnlyList<JobSettings>? Jobs);

internal sealed record JobSettings(
    string PathToModels,
    IReadOnlyList<ResourceSettings> Models,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyDictionary<string, string>? Templates = null,
    IReadOnlyDictionary<string, string>? OutputDirectory = null,
    bool OverwriteExisting = false,
    bool DeleteGenerated = false);

internal sealed record ResourceSettings(
    string Aggregate,
    string Route,
    IReadOnlyList<string> DetailsFields,
    IReadOnlyList<string> ListFields,
    IReadOnlyList<ChildSettings>? Children = null);

internal sealed record ChildSettings(
    string Collection,
    string Resource,
    string Route,
    IReadOnlyList<string> DetailsFields,
    IReadOnlyList<string> ListFields,
    string? CreateMethod = null,
    string? UpdateMethod = null,
    string? DeleteMethod = null);
