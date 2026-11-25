using PANiXiDA.Core.Backend.CodeGenerator.Configuration;

namespace PANiXiDA.Core.Backend.CodeGenerator.Generation.Domain;

internal sealed record ResourceTarget(DomainProject Project, string Feature, string Resource)
{
    public string Domain => $"{Project.Module}.{ModuleLayer.Domain}.{Feature}";

    public string Aggregate => $"{Domain}.{Resource}";

    internal ResourceTarget ForChild(ChildSettings? child)
    {
        if (child is null
            || !DomainProject.IsIdentifier(child.Collection)
            || !DomainProject.IsIdentifier(child.Resource))
        {
            throw new InvalidOperationException(
                $"'{Aggregate}' requires a valid collection and resource for each child.");
        }

        return new ResourceTarget(Project, Resource + child.Collection, child.Resource);
    }

    internal string DefaultDirectory(string layer) => GetDefaultDirectory(layer, required: true)!;

    internal string? FindDefaultDirectory(string layer) => GetDefaultDirectory(layer, required: false);

    internal string ExpandOutputDirectory(string directory) =>
        directory.Replace("{Module}", Project.Module, StringComparison.Ordinal)
            .Replace("{ModuleName}", Project.ModuleName, StringComparison.Ordinal)
            .Replace("{Feature}", Feature, StringComparison.Ordinal)
            .Replace("{Resource}", Resource, StringComparison.Ordinal);

    private string? GetDefaultDirectory(string layer, bool required)
    {
        var domainDirectory = Path.GetDirectoryName(Project.ProjectFile)!;
        if (layer == ModuleLayer.Domain)
        {
            return Path.Combine(domainDirectory, Feature);
        }

        var moduleDirectory = Path.GetDirectoryName(domainDirectory);
        if (moduleDirectory is null)
        {
            return required
                ? throw new InvalidOperationException($"'{Project.ProjectFile}' has no module directory.")
                : null;
        }

        var projectName = $"{Project.Module}.{layer}";
        var projectDirectory = Path.Combine(moduleDirectory, projectName);

        if (!File.Exists(Path.Combine(projectDirectory, $"{projectName}.csproj")))
        {
            return required
                ? throw new InvalidOperationException($"Project '{projectName}' was not found in '{moduleDirectory}'.")
                : null;
        }

        return layer switch
        {
            ModuleLayer.Application => Path.Combine(projectDirectory, Feature),
            ModuleLayer.Infrastructure => Path.Combine(projectDirectory, "Persistence", "Features", Feature),
            ModuleLayer.Presentation => Path.Combine(projectDirectory, "Features", Feature),
            _ => throw new InvalidOperationException($"Unknown template layer '{layer}'.")
        };
    }
}
