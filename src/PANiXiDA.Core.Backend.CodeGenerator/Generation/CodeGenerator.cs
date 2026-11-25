using System.Reflection;
using System.Runtime.Loader;
using System.Text;

using Scriban;
using Scriban.Runtime;
using Scriban.Syntax;

using PANiXiDA.Core.Backend.CodeGenerator.FileSystem;
using PANiXiDA.Core.Backend.CodeGenerator.Templating;

namespace PANiXiDA.Core.Backend.CodeGenerator.Generation;

internal static class CodeGenerator
{
    internal static async Task<IReadOnlyList<OutputChange>> RunJobsAsync(
        RepositoryLayout repository,
        IReadOnlyList<JobSettings> jobs,
        CancellationToken cancellationToken)
    {
        using var plan = new GenerationPlan(repository);
        foreach (var job in jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await plan.AddJobAsync(job, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var changes = plan.Changes;
        OutputChange.ApplyAll(changes);
        return changes;
    }

    private sealed class GenerationPlan(RepositoryLayout repository) : IDisposable
    {
        private readonly Dictionary<string, TemplateArtifact> _catalog = LoadCatalog(EmbeddedTemplates.Paths);
        private readonly Dictionary<string, Template> _parsedTemplates = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Assembly> _domains = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _routes = new(StringComparer.Ordinal);

        internal List<OutputChange> Changes { get; } = [];

        internal async Task AddJobAsync(JobSettings job, CancellationToken cancellationToken)
        {
            ValidateJob(job);
            var artifacts = job.GeneratedFiles.Select(name => _catalog[name]).ToArray();
            var rootArtifacts = artifacts.Where(artifact => !artifact.Child).ToArray();
            var childArtifacts = artifacts.Where(artifact => artifact.Child).ToArray();
            var project = DomainProject.Find(repository, job.PathToModels);
            var assembly = job.DeleteGenerated
                ? null
                : await GetDomainAsync(project, cancellationToken);

            foreach (var settings in job.Models)
            {
                AddModel(job, project, assembly, settings, rootArtifacts, childArtifacts);
            }
        }

        public void Dispose()
        {
            foreach (var domain in _domains.Values)
            {
                AssemblyLoadContext.GetLoadContext(domain)!.Unload();
            }
        }

        private async Task<Assembly> GetDomainAsync(DomainProject project, CancellationToken cancellationToken)
        {
            if (!_domains.TryGetValue(project.ProjectFile, out var assembly))
            {
                assembly = await project.BuildAsync(repository.Root, cancellationToken);
                _domains.Add(project.ProjectFile, assembly);
            }

            return assembly;
        }

        private void AddModel(
            JobSettings job,
            DomainProject project,
            Assembly? assembly,
            ResourceSettings? settings,
            TemplateArtifact[] rootArtifacts,
            TemplateArtifact[] childArtifacts)
        {
            if (settings is null)
            {
                throw new InvalidOperationException("The models array cannot contain null entries.");
            }

            var target = project.Locate(settings.Aggregate);
            if (rootArtifacts.Length > 0)
            {
                var model = assembly is null ? null : ResourceModel.Create(assembly, target, settings);
                AddArtifacts(target, target.Aggregate, model, job, rootArtifacts);
            }

            if (childArtifacts.Length == 0)
            {
                return;
            }

            AddChildren(target, assembly, settings, job, childArtifacts);
        }

        private void AddChildren(
            ResourceTarget target,
            Assembly? assembly,
            ResourceSettings settings,
            JobSettings job,
            TemplateArtifact[] artifacts)
        {
            if (settings.Children is not { Count: > 0 })
            {
                throw new InvalidOperationException($"'{target.Aggregate}' requires children for Child artifacts.");
            }

            var childNames = artifacts.Select(artifact => artifact.Name).ToArray();
            foreach (var child in settings.Children)
            {
                var childTarget = target.ForChild(child);
                var model = assembly is null
                    ? null
                    : ResourceModel.CreateChild(assembly, target, childTarget, settings.Route, child, childNames);
                AddArtifacts(childTarget, $"{target.Aggregate}.{child.Collection}", model, job, artifacts);
            }
        }

        private void AddArtifacts(
            ResourceTarget target, string owner, ResourceModel? model, JobSettings job, TemplateArtifact[] artifacts)
        {
            if (model is not null && artifacts.Any(artifact => artifact.Name is "Endpoints" or "ChildEndpoints"))
            {
                var route = model.Child is null
                    ? model.Route
                    : model.Route.Replace(model.Child.RouteId + ":guid", "parentId:guid", StringComparison.Ordinal);
                RegisterRoute(_routes, route, owner);
            }

            foreach (var artifact in artifacts)
            {
                var path = GetOutputPath(repository, target, job, artifact);
                if (!_paths.Add(path) || Directory.Exists(path))
                {
                    throw new InvalidOperationException(
                        $"'{GetRelativePath(repository, path)}' is not a unique file path.");
                }

                var change = PlanChange(repository, owner, model, job, artifact, path, _parsedTemplates);
                if (change is not null)
                {
                    Changes.Add(change);
                }
            }
        }

        private void ValidateJob(JobSettings? job)
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

            if (HasEmptyPath(job.Templates) || HasEmptyPath(job.OutputDirectory))
            {
                throw new InvalidOperationException("Template and output directory overrides must be nonempty paths.");
            }

            var invalidArtifact = job.GeneratedFiles.Concat(job.OutputDirectory?.Keys ?? [])
                .Where(name => name is null || !_catalog.ContainsKey(name))
                .Select(name => name ?? string.Empty)
                .FirstOrDefault();
            if (invalidArtifact is not null)
            {
                throw new InvalidOperationException($"Unknown generated file '{invalidArtifact}'.");
            }

            var invalidTemplate = (job.Templates?.Keys ?? [])
                .FirstOrDefault(name => !_catalog.ContainsKey(name) && !PartialTemplateLoader.IsBundled(name));
            if (invalidTemplate is not null)
            {
                throw new InvalidOperationException($"Unknown template '{invalidTemplate}'.");
            }
        }

        private static bool HasEmptyPath(IReadOnlyDictionary<string, string>? overrides) =>
            overrides is not null && overrides.Values.Any(string.IsNullOrWhiteSpace);

        private static void RegisterRoute(Dictionary<string, string> routes, string route, string aggregate)
        {
            if (!routes.TryAdd(route, aggregate) && routes[route] != aggregate)
            {
                throw new InvalidOperationException($"The route '{route}' is configured for more than one aggregate.");
            }
        }

        private static string GetOutputPath(
            RepositoryLayout repository,
            ResourceTarget target,
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
            var label = GetRelativePath(repository, path);
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

        private static string GetRelativePath(RepositoryLayout repository, string path) =>
            Path.GetRelativePath(repository.Root, path);
    }

    internal static Dictionary<string, TemplateArtifact> LoadCatalog(IEnumerable<string> paths)
    {
        var catalog = new Dictionary<string, TemplateArtifact>(StringComparer.Ordinal);
        foreach (var path in paths.Where(path => path.EndsWith(".cs.sbn", StringComparison.Ordinal)))
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

    internal sealed record TemplateArtifact(
        string Name, string Layer, string RelativePath, string SourcePath, bool Child);
}
