namespace PANiXiDA.Core.Backend.CodeGenerator.Generation;

internal sealed record CodeTarget(DomainProject Project, string Feature, string Resource)
{
    public string Domain => $"{Project.Module}.Domain.{Feature}";

    public string Aggregate => $"{Domain}.{Resource}";

    internal CodeTarget ForChild(ChildSettings? child)
    {
        if (child is null
            || !DomainProject.IsIdentifier(child.Collection)
            || !DomainProject.IsIdentifier(child.Resource))
        {
            throw new InvalidOperationException(
                $"'{Aggregate}' requires a valid collection and resource for each child.");
        }

        return new CodeTarget(Project, Resource + child.Collection, child.Resource);
    }

    internal string DefaultDirectory(string layer)
    {
        var domainDirectory = Path.GetDirectoryName(Project.ProjectFile)!;
        if (layer == "Domain")
        {
            return Path.Combine(domainDirectory, Feature);
        }

        var moduleDirectory = Path.GetDirectoryName(domainDirectory)
            ?? throw new InvalidOperationException($"'{Project.ProjectFile}' has no module directory.");
        var projectName = $"{Project.Module}.{layer}";
        var projectDirectory = Path.Combine(moduleDirectory, projectName);

        if (!File.Exists(Path.Combine(projectDirectory, $"{projectName}.csproj")))
        {
            throw new InvalidOperationException($"Project '{projectName}' was not found in '{moduleDirectory}'.");
        }

        return layer switch
        {
            "Application" => Path.Combine(projectDirectory, Feature),
            "Infrastructure" => Path.Combine(projectDirectory, "Persistence", "Features", Feature),
            "Presentation" => Path.Combine(projectDirectory, "Features", Feature),
            _ => throw new InvalidOperationException($"Unknown template layer '{layer}'.")
        };
    }

    internal string ExpandOutputDirectory(string directory) =>
        directory.Replace("{Module}", Project.Module, StringComparison.Ordinal)
            .Replace("{ModuleName}", Project.ModuleName, StringComparison.Ordinal)
            .Replace("{Feature}", Feature, StringComparison.Ordinal)
            .Replace("{Resource}", Resource, StringComparison.Ordinal);
}
