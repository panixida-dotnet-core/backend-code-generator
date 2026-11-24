using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace PANiXiDA.Core.Backend.CodeGenerator;

internal sealed class PartialTemplateLoader(
    RepositoryLayout repository,
    IReadOnlyDictionary<string, string>? overrides) : ITemplateLoader
{
    internal static bool IsBundled(string name) => EmbeddedTemplates.Contains($"Partials/{name}");

    public string GetPath(TemplateContext context, SourceSpan callerSpan, string templateName)
    {
        if (!IsBundled(templateName))
        {
            throw new InvalidOperationException($"Unknown template partial '{templateName}'.");
        }

        return overrides?.TryGetValue(templateName, out var path) == true
            ? repository.ResolvePath(path, repository.ConfigurationDirectory)
            : $"Partials/{templateName}";
    }

    public string Load(TemplateContext context, SourceSpan callerSpan, string templatePath) =>
        Path.IsPathFullyQualified(templatePath) ? File.ReadAllText(templatePath) : EmbeddedTemplates.Read(templatePath);

    public async ValueTask<string?> LoadAsync(TemplateContext context, SourceSpan callerSpan, string templatePath)
    {
        if (Path.IsPathFullyQualified(templatePath))
        {
            return await File.ReadAllTextAsync(templatePath);
        }

        using var reader = new StreamReader(EmbeddedTemplates.Open(templatePath));
        return await reader.ReadToEndAsync();
    }
}
