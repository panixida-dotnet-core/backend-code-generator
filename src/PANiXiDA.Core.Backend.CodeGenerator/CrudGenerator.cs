using System.Reflection;
using System.Runtime.Loader;
using System.Text;

using Scriban;
using Scriban.Runtime;
using Scriban.Syntax;

namespace PANiXiDA.Core.Backend.CodeGenerator;

internal static class CrudGenerator
{
    internal static async Task<IReadOnlyList<OutputChange>> RunJobsAsync(
        RepositoryLayout repository,
        IReadOnlyList<JobSettings> jobs,
        CancellationToken cancellationToken)
    {
        var catalog = LoadCatalog();
        var parsedTemplates = new Dictionary<string, Template>(StringComparer.Ordinal);
        var domains = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var routes = new Dictionary<string, string>(StringComparer.Ordinal);
        var changes = new List<OutputChange>();
        try
        {
            foreach (var job in jobs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateJob(job, catalog);
                var artifacts = job.GeneratedFiles.Select(name => catalog[name]).ToArray();
                var rootArtifacts = artifacts.Where(artifact => !artifact.Child).ToArray();
                var childArtifacts = artifacts.Where(artifact => artifact.Child).ToArray();
                var childNames = childArtifacts.Select(artifact => artifact.Name).ToArray();
                var project = DomainProject.Find(repository, job.PathToModels);
                var assembly = job.DeleteGenerated
                        ? null
                        : await GetDomainAsync(repository, project, domains, cancellationToken);
                foreach (var settings in job.Models)
                {
                    if (settings is null)
                    {
                        throw new InvalidOperationException("The models array cannot contain null entries.");
                    }

                    var target = project.Locate(settings.Aggregate);
                    if (rootArtifacts.Length > 0)
                    {
                        var model = assembly is null ? null : ResourceModel.Create(assembly, target, settings);
                        Plan(target, target.Aggregate, model, job, rootArtifacts);
                    }

                    if (childArtifacts.Length == 0)
                    {
                        continue;
                    }

                    if (settings.Children is not { Count: > 0 })
                    {
                        throw new InvalidOperationException($"'{target.Aggregate}' requires children for Child artifacts.");
                    }

                    foreach (var child in settings.Children)
                    {
                        var childTarget = target.ForChild(child);
                        var model = assembly is null
                            ? null
                            : ResourceModel.CreateChild(assembly, target, childTarget, settings.Route, child, childNames);
                        Plan(childTarget, $"{target.Aggregate}.{child.Collection}", model, job, childArtifacts);
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            OutputChange.ApplyAll(changes);
            return changes;
        }
        finally
        {
            foreach (var domain in domains.Values)
            {
                AssemblyLoadContext.GetLoadContext(domain)?.Unload();
            }
        }

        void Plan(
            CrudTarget target, string owner, ResourceModel? model, JobSettings job, TemplateArtifact[] artifacts)
        {
            if (model is not null && artifacts.Any(artifact => artifact.Name is "Endpoints" or "ChildEndpoints"))
            {
                var route = model.Child is null
                    ? model.Route
                    : model.Route.Replace(model.Child.RouteId + ":guid", "parentId:guid", StringComparison.Ordinal);
                RegisterRoute(routes, route, owner);
            }

            foreach (var artifact in artifacts)
            {
                var path = GetOutputPath(repository, target, job, artifact);
                if (!paths.Add(path) || Directory.Exists(path))
                {
                    throw new InvalidOperationException(
                        $"'{RelativePath(repository, path)}' is not a unique file path.");
                }

                var change = PlanChange(repository, owner, model, job, artifact, path, parsedTemplates);
                if (change is not null)
                {
                    changes.Add(change);
                }
            }
        }
    }

    private static async Task<Assembly> GetDomainAsync(
        RepositoryLayout repository,
        DomainProject project,
        Dictionary<string, Assembly> domains,
        CancellationToken cancellationToken)
    {
        if (!domains.TryGetValue(project.ProjectFile, out var assembly))
        {
            assembly = await project.BuildAsync(repository.Root, cancellationToken);
            domains.Add(project.ProjectFile, assembly);
        }

        return assembly;
    }

    private static void ValidateJob(JobSettings? job, IReadOnlyDictionary<string, TemplateArtifact> catalog)
    {
        if (job is null
            || job.Models is not { Count: > 0 }
            || job.GeneratedFiles is not { Count: > 0 })
        {
            throw new InvalidOperationException("Each job requires nonempty models and generatedFiles arrays.");
        }

        if (job.OverwriteExisting && job.DeleteGenerated)
        {
            throw new InvalidOperationException(
                "overwriteExisting and deleteGenerated cannot both be true in one job.");
        }

        if (job.Templates?.Values.Any(string.IsNullOrWhiteSpace) == true
            || job.OutputDirectory?.Values.Any(string.IsNullOrWhiteSpace) == true)
        {
            throw new InvalidOperationException("Template and output directory overrides must be nonempty paths.");
        }

        var names = job.GeneratedFiles.Concat(job.OutputDirectory?.Keys ?? []);
        foreach (var name in names)
        {
            if (name is null || !catalog.ContainsKey(name))
            {
                throw new InvalidOperationException($"Unknown generated file '{name}'.");
            }
        }

        foreach (var name in job.Templates?.Keys ?? [])
        {
            if (!catalog.ContainsKey(name) && !PartialTemplateLoader.IsBundled(name))
            {
                throw new InvalidOperationException($"Unknown template '{name}'.");
            }
        }
    }

    private static void RegisterRoute(Dictionary<string, string> routes, string route, string aggregate)
    {
        if (!routes.TryAdd(route, aggregate) && routes[route] != aggregate)
        {
            throw new InvalidOperationException($"The route '{route}' is configured for more than one aggregate.");
        }
    }

    private static string GetOutputPath(
        RepositoryLayout repository,
        CrudTarget target,
        JobSettings job,
        TemplateArtifact artifact)
    {
        var file = artifact.RelativePath.Replace("__Resource__", target.Resource, StringComparison.Ordinal)
            .Replace("__Feature__", target.Feature, StringComparison.Ordinal);
        if (job.OutputDirectory?.TryGetValue(artifact.Name, out var configuredDirectory) == true)
        {
            var expanded = target.ExpandOutputDirectory(configuredDirectory);
            var destination = repository.ResolvePath(expanded, repository.Root);
            return repository.ResolvePath(Path.Combine(destination, Path.GetFileName(file)), repository.Root);
        }

        return repository.ResolvePath(Path.Combine(target.DefaultDirectory(artifact.Layer), file), repository.Root);
    }

    private static OutputChange? PlanChange(
        RepositoryLayout repository,
        string owner,
        ResourceModel? model,
        JobSettings job,
        TemplateArtifact artifact,
        string path,
        Dictionary<string, Template> parsedTemplates)
    {
        var previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var label = RelativePath(repository, path);
        var header = $"// Scaffolded by CodeGenerator for {owner}.";
        if (job.DeleteGenerated)
        {
            if (previous is null)
            {
                return null;
            }

            var firstLine = Encoding.UTF8.GetString(previous).Split('\n', 2)[0].Trim('\uFEFF', '\r');
            if (firstLine != header)
            {
                throw new InvalidOperationException($"Cannot delete '{label}': its scaffold marker does not match.");
            }

            return new OutputChange(path, previous, null);
        }

        if (previous is not null && !job.OverwriteExisting)
        {
            throw new InvalidOperationException(
                $"Cannot replace '{label}': it already exists and overwriteExisting is false.");
        }

        var templatePath = job.Templates?.TryGetValue(artifact.Name, out var overridePath) == true
            ? repository.ResolvePath(overridePath, repository.ConfigurationDirectory)
            : artifact.SourcePath;
        var partials = new PartialTemplateLoader(repository, job.Templates);
        var source = Render(templatePath, model, parsedTemplates, partials);
        return new OutputChange(path, previous, Encoding.UTF8.GetBytes($"{header}\n\n{source}"));
    }

    private static string Render(
        string path,
        ResourceModel? model,
        Dictionary<string, Template> templates,
        PartialTemplateLoader partials)
    {
        if (!templates.TryGetValue(path, out var template))
        {
            var contents = Path.IsPathFullyQualified(path) ? File.ReadAllText(path) : EmbeddedTemplates.Read(path);
            template = Template.Parse(contents, path);
            if (template.HasErrors)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, template.Messages));
            }

            templates.Add(path, template);
        }

        var context = new TemplateContext
        {
            StrictVariables = true,
            EnableRelaxedMemberAccess = false,
            TemplateLoader = partials
        };
        context.PushGlobal(new ScriptObject { ["model"] = model });
        try
        {
            return template.Render(context).ReplaceLineEndings("\n").TrimEnd() + "\n";
        }
        catch (ScriptRuntimeException exception)
        {
            throw new InvalidOperationException($"Could not render template '{path}': {exception.Message}", exception);
        }
    }

    private static Dictionary<string, TemplateArtifact> LoadCatalog()
    {
        var catalog = new Dictionary<string, TemplateArtifact>(StringComparer.Ordinal);
        foreach (var path in EmbeddedTemplates.Paths.Where(path => path.EndsWith(".cs.sbn", StringComparison.Ordinal)))
        {
            var separator = path.IndexOf('/');
            if (separator < 0
                || path[..separator] is not ("Domain" or "Application" or "Infrastructure" or "Presentation"))
            {
                continue;
            }

            var outputFile = path[(separator + 1)..^".sbn".Length];
            var name = Path.GetFileNameWithoutExtension(outputFile)
                .Replace("__Resource__", string.Empty, StringComparison.Ordinal)
                .Replace("__Feature__", string.Empty, StringComparison.Ordinal);
            var artifact = new TemplateArtifact(name, path[..separator], outputFile, path, Child: false);
            if (!catalog.TryAdd(name, artifact))
            {
                throw new InvalidOperationException($"Duplicate artifact name '{name}' in the bundled templates.");
            }

            if (name is not ("IRepository" or "Repository"))
            {
                var child = artifact with { Name = $"Child{name}", Child = true };
                catalog.Add(child.Name, child);
            }
        }

        return catalog;
    }

    private static string RelativePath(RepositoryLayout repository, string path) =>
        Path.GetRelativePath(repository.Root, path);

    private sealed record TemplateArtifact(
        string Name, string Layer, string RelativePath, string SourcePath, bool Child);
}
