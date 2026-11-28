using System.Reflection;

using PANiXiDA.Core.Backend.CodeGenerator.Generation.Domain;

namespace PANiXiDA.Core.Backend.CodeGenerator.Generation.Models;

internal sealed record WriteHandlerTypes(
    IReadOnlyList<string> Imports,
    IReadOnlyList<string> Aliases,
    string AggregateType,
    string IdType,
    string? ParentIdType,
    IReadOnlyDictionary<string, string> DomainTypes)
{
    private static readonly string[] s_handlerTypes =
    [
        "CancellationToken", "Error", "Guid", "ICommandHandler`2", "IRequireTransaction", "Result", "Result`1", "Task", "Task`1"
    ];

    internal static WriteHandlerTypes Create(ResourceModel model, Type resource, Type id, Type? parentId, bool isUpdate)
    {
        var operation = isUpdate ? "Update" : "Create";
        var fields = isUpdate ? model.UpdateFields : model.CreateFields;
        var domainTypes = fields.Where(field => field.BindsDomainValue)
            .Select(field => resource.GetProperty(field.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.PropertyType)
            .Select(type => Nullable.GetUnderlyingType(type) ?? type)
            .Prepend(parentId ?? resource);
        if (isUpdate)
        {
            domainTypes = domainTypes.Append(id);
        }

        var types = domainTypes.Distinct().ToArray();
        var assemblies = types.Select(type => type.Assembly).Distinct().ToArray();
        var repositoryNamespace = model.AggregateDomain + ".Abstractions";
        var handlerTypes = s_handlerTypes.Append($"I{model.AggregateFeature}Repository").ToArray();
        var reservedNames = model.Module.Split('.')
            .Concat(
            [
                ModuleLayer.Domain, ModuleLayer.Application, ModuleLayer.Infrastructure, ModuleLayer.Presentation,
                model.Feature, model.AggregateFeature, "Read", "Write", "Create", "Update", "Delete"
            ])
            .Concat(handlerTypes.Select(name => name.Split('`')[0]))
            .Append($"{operation}{model.Resource}Handler")
            .Append($"{operation}{model.Resource}Command")
            .ToHashSet(StringComparer.Ordinal);
        var namespaces = types.Select(type => type.Namespace).Distinct(StringComparer.Ordinal)
            .Where(@namespace => !handlerTypes.Any(name => ExposesType(assemblies, @namespace, name)))
            .Append(repositoryNamespace)
            .ToHashSet(StringComparer.Ordinal);
        var imports = new SortedSet<string>(StringComparer.Ordinal) { repositoryNamespace };
        var aliases = new List<string>();
        var names = new Dictionary<Type, string>();
        var usedNames = types.Select(type => type.Name).Concat(reservedNames).ToHashSet(StringComparer.Ordinal);
        foreach (var type in types)
        {
            var shortName = type.Name;
            if (reservedNames.Contains(shortName) || WriteAggregateNaming.ConflictsWithSdkType(shortName)
                || !namespaces.Contains(type.Namespace)
                || namespaces.Any(@namespace => @namespace != type.Namespace && ExposesType(assemblies, @namespace, shortName)))
            {
                var prefix = type == resource ? "DomainAggregate" : $"Domain{shortName}";
                var alias = ReserveAlias(prefix, usedNames);
                names.Add(type, alias);
                aliases.Add($"{alias} = {type.FullName}");
                continue;
            }

            names.Add(type, shortName);
            if (type.Namespace is { } @namespace)
            {
                imports.Add(@namespace);
            }
        }

        return new WriteHandlerTypes(
            [.. imports],
            [.. aliases.Order(StringComparer.Ordinal)],
            names.GetValueOrDefault(resource, resource.Name),
            names.GetValueOrDefault(id, id.Name),
            parentId is null ? null : names[parentId],
            names.ToDictionary(pair => $"global::{pair.Key.FullName}", pair => pair.Value, StringComparer.Ordinal));
    }

    private static bool ExposesType(IEnumerable<Assembly> assemblies, string? @namespace, string name)
    {
        var qualifiedName = @namespace is null ? name : $"{@namespace}.{name}";
        return assemblies.Any(assembly => assembly.GetType(qualifiedName) is { IsPublic: true });
    }

    private static string ReserveAlias(string prefix, HashSet<string> usedNames)
    {
        var alias = prefix;
        for (var suffix = 2; !usedNames.Add(alias); suffix++)
        {
            alias = $"{prefix}{suffix}";
        }

        return alias;
    }
}
