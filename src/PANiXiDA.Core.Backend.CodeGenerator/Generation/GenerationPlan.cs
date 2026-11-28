using System.Reflection;
using System.Runtime.Loader;
using System.Text;

using Scriban;
using Scriban.Runtime;
using Scriban.Syntax;

using PANiXiDA.Core.Backend.CodeGenerator.Configuration;
using PANiXiDA.Core.Backend.CodeGenerator.FileSystem;
using PANiXiDA.Core.Backend.CodeGenerator.Generation.Domain;
using PANiXiDA.Core.Backend.CodeGenerator.Generation.Models;
using PANiXiDA.Core.Backend.CodeGenerator.Templating;

namespace PANiXiDA.Core.Backend.CodeGenerator.Generation;

internal sealed class GenerationPlan(RepositoryLayout repository) : IDisposable
{
    private const string ResourcePlaceholder = "__Resource__";
    private const string FeaturePlaceholder = "__Feature__";
    private const string ChildArtifactPrefix = "Child";
    private const string EndpointsArtifactName = "Endpoints";
    private const string ChildEndpointsArtifactName = ChildArtifactPrefix + EndpointsArtifactName;
    private const string ChildReadRepositoryName = "ChildReadRepository";

    private readonly Dictionary<string, TemplateArtifact> _catalog = LoadCatalog(EmbeddedTemplates.Paths);
    private readonly Dictionary<string, Template> _parsedTemplates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Assembly Assembly, AssemblyLoadContext Context)> _domains =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _routes = new(StringComparer.Ordinal);
    private readonly List<PlannedArtifact> _outputs = [];

    public void Dispose()
    {
        foreach (var domain in _domains.Values)
        {
            domain.Context.Unload();
        }
    }

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

    internal List<OutputChange> BuildChanges(CancellationToken cancellationToken)
    {
        var changes = new List<OutputChange>(_outputs.Count);
        foreach (var output in _outputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = GetTemplateModel(output);
            var change = PlanChange(
                repository, model, output.Job, output.Artifact, output.Path, _parsedTemplates);
            if (change is not null)
            {
                changes.Add(change);
            }
        }

        return changes;
    }

    internal static Dictionary<string, TemplateArtifact> LoadCatalog(IEnumerable<string> paths)
    {
        var catalog = new Dictionary<string, TemplateArtifact>(StringComparer.Ordinal);
        foreach (var path in paths.Where(path => path.EndsWith(".cs.sbn", StringComparison.Ordinal)))
        {
            var separator = path.IndexOf('/');
            if (separator < 0
                || path[..separator] is not (ModuleLayer.Domain or ModuleLayer.Application
                    or ModuleLayer.Infrastructure or ModuleLayer.Presentation))
            {
                continue;
            }

            var outputFile = path[(separator + 1)..^".sbn".Length];
            var name = Path.GetFileNameWithoutExtension(outputFile)
                .Replace(ResourcePlaceholder, string.Empty, StringComparison.Ordinal)
                .Replace(FeaturePlaceholder, string.Empty, StringComparison.Ordinal);
            var artifact = new TemplateArtifact(name, path[..separator], outputFile, path, Child: false);
            if (!catalog.TryAdd(name, artifact))
            {
                throw new InvalidOperationException($"Duplicate artifact name '{name}' in the bundled templates.");
            }

            if (name is not ("IRepository" or "Repository" or "IReadRepository"))
            {
                var child = artifact with { Name = ChildArtifactPrefix + name, Child = true };
                catalog.Add(child.Name, child);
            }
        }

        return catalog;
    }

    private async Task<Assembly> GetDomainAsync(DomainProject project, CancellationToken cancellationToken)
    {
        if (!_domains.TryGetValue(project.ProjectFile, out var domain))
        {
            domain = await project.BuildAsync(repository.Root, cancellationToken);
            _domains.Add(project.ProjectFile, domain);
        }

        return domain.Assembly;
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
            var model = assembly is null
                ? null
                : ResourceModelFactory.Create(
                    assembly,
                    target,
                    settings,
                    includeCreate: rootArtifacts.Any(artifact => artifact.Name.StartsWith("Create", StringComparison.Ordinal)),
                    includeUpdate: rootArtifacts.Any(artifact => artifact.Name.StartsWith("Update", StringComparison.Ordinal)));
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
                : ResourceModelFactory.CreateChild(assembly, target, childTarget, settings.Route, child, childNames);
            AddArtifacts(childTarget, $"{target.Aggregate}.{child.Collection}", model, job, artifacts, target);
        }
    }

    private void AddArtifacts(
        ResourceTarget target, string owner, ResourceModel? model, JobSettings job, TemplateArtifact[] artifacts,
        ResourceTarget? parent = null)
    {
        if (model is not null
            && artifacts.Any(artifact => artifact.Name is EndpointsArtifactName or ChildEndpointsArtifactName))
        {
            var route = model.Child is null
                ? model.Route
                : model.Route.Replace(model.Child.RouteId.ToLowerInvariant() + ":guid", "parentid:guid", StringComparison.Ordinal);
            RegisterRoute(_routes, route, owner);
        }

        foreach (var artifact in artifacts)
        {
            var path = GetOutputPath(repository, target, job, artifact, parent: parent)!;
            if (!_paths.Add(path) || Directory.Exists(path))
            {
                throw new InvalidOperationException(
                    $"'{GetRelativePath(repository, path)}' is not a unique file path.");
            }

            _outputs.Add(new PlannedArtifact(target, owner, model, job, artifact, path, parent));
        }
    }

    private ResourceModel? GetTemplateModel(PlannedArtifact output)
    {
        if (output.Model is not { } model)
        {
            return null;
        }

        var name = output.Artifact.Child ? output.Artifact.Name[ChildArtifactPrefix.Length..] : output.Artifact.Name;
        var isReadRepository = name is "IReadRepository" or "ReadRepository";
        var hasListItemReadModel = isReadRepository && HasArtifact(output, "ListItemReadModel");
        var usesFilter = hasListItemReadModel
            || name is "GetListQuery" or "GetListQueryValidator" or "GetListHandler" or "GetListEndpoint" or "GetListMapper";

        return model with
        {
            HasDetailsReadModel = isReadRepository && HasArtifact(output, "DetailsReadModel"),
            HasListItemReadModel = hasListItemReadModel,
            HasDetailsEndpoint = name == "CreateEndpoint" && HasArtifact(output, "GetDetailsEndpoint"),
            HasFilter = usesFilter && HasArtifact(output, "Filter"),
            HasFilterValidator = name == "GetListQueryValidator" && HasArtifact(output, "FilterValidator"),
            ChildReadContracts = name == "IReadRepository" ? GetChildReadContracts(output) : []
        };
    }

    private ResourceModel[] GetChildReadContracts(PlannedArtifact output)
    {
        var artifact = _catalog[ChildReadRepositoryName];
        var children = _outputs
            .Where(candidate => candidate.Parent == output.Target && candidate.Artifact == artifact && candidate.Model is not null)
            .DistinctBy(candidate => candidate.Target)
            .ToDictionary(candidate => candidate.Target);
        var settings = output.Job.Models.Single(model => model.Aggregate == output.Owner);
        foreach (var child in settings.Children ?? [])
        {
            var target = output.Target.ForChild(child);
            if (children.ContainsKey(target)
                || !HasArtifact(target, output.Job, ChildReadRepositoryName, output.Target))
            {
                continue;
            }

            var model = ResourceModelFactory.CreateChild(
                _domains[output.Target.Project.ProjectFile].Assembly, output.Target, target, settings.Route, child, []);
            children.Add(target, output with { Target = target, Model = model, Artifact = artifact, Parent = output.Target });
        }

        return [.. children.Values.Select(child => GetTemplateModel(child)!)];
    }

    private bool HasArtifact(PlannedArtifact output, string name)
    {
        var artifactName = output.Artifact.Child ? ChildArtifactPrefix + name : name;
        return HasArtifact(output.Target, output.Job, artifactName, output.Parent);
    }

    private bool HasArtifact(ResourceTarget target, JobSettings job, string artifactName, ResourceTarget? parent)
    {
        if (_outputs.Any(candidate => candidate.Target == target
            && candidate.Artifact.Name == artifactName && !candidate.Job.DeleteGenerated))
        {
            return true;
        }

        var path = GetOutputPath(repository, target, job, _catalog[artifactName], required: false, parent: parent);
        return path is not null && !_paths.Contains(path) && File.Exists(path);
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

    private static string? GetOutputPath(
        RepositoryLayout repository,
        ResourceTarget target,
        JobSettings job,
        TemplateArtifact artifact,
        bool required = true,
        ResourceTarget? parent = null)
    {
        var repositoryParent = artifact.Name == ChildReadRepositoryName ? parent : null;
        var belongsToParent = (artifact.Layer is ModuleLayer.Application or ModuleLayer.Infrastructure
            && artifact.RelativePath.StartsWith("Read/", StringComparison.Ordinal))
            || artifact.Name == "ChildConfiguration";
        var directoryTarget = repositoryParent ?? (belongsToParent ? parent : null) ?? target;
        var file = artifact.RelativePath.Replace(ResourcePlaceholder, target.Resource, StringComparison.Ordinal)
            .Replace(FeaturePlaceholder, repositoryParent?.Feature ?? target.Feature, StringComparison.Ordinal);
        if (repositoryParent is not null)
        {
            file = $"{file[..^".cs".Length]}.{target.Feature}.cs";
        }

        if (job.OutputDirectory?.TryGetValue(artifact.Name, out var configuredDirectory) == true)
        {
            var expanded = target.ExpandOutputDirectory(configuredDirectory);
            var destination = repository.ResolvePath(expanded, repository.Root);
            return repository.ResolvePath(Path.Combine(destination, Path.GetFileName(file)), repository.Root);
        }

        var directory = required
            ? directoryTarget.DefaultDirectory(artifact.Layer)
            : directoryTarget.FindDefaultDirectory(artifact.Layer);
        return directory is null ? null : repository.ResolvePath(Path.Combine(directory, file), repository.Root);
    }

    private static OutputChange? PlanChange(
        RepositoryLayout repository,
        ResourceModel? model,
        JobSettings job,
        TemplateArtifact artifact,
        string path,
        Dictionary<string, Template> parsedTemplates)
    {
        var previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var label = GetRelativePath(repository, path);
        if (job.DeleteGenerated)
        {
            return previous is null ? null : new OutputChange(path, previous, null);
        }

        if (previous is not null && !job.OverwriteExisting)
        {
            throw new InvalidOperationException(
                $"Cannot replace '{label}': it already exists and overwriteExisting is false.");
        }

        var templatePath = job.Templates?.TryGetValue(artifact.Name, out var overridePath) == true
            ? repository.ResolvePath(overridePath, repository.TemplateBaseDirectory)
            : artifact.SourcePath;
        var partials = new PartialTemplateLoader(repository, job.Templates);
        var source = Render(templatePath, model, parsedTemplates, partials);
        return new OutputChange(path, previous, Encoding.UTF8.GetBytes(source));
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
        var globals = new ScriptObject { ["model"] = model };
        globals.Import("csharp_argument", (Func<string, bool, string>)CSharpIdentifier.Escape);
        context.PushGlobal(globals);
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

    private sealed record PlannedArtifact(
        ResourceTarget Target, string Owner, ResourceModel? Model, JobSettings Job, TemplateArtifact Artifact,
        string Path, ResourceTarget? Parent);
}
