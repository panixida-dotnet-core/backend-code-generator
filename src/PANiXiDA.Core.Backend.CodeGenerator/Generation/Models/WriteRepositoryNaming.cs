using System.Collections.Frozen;

using PANiXiDA.Core.Backend.CodeGenerator.Generation.Domain;

namespace PANiXiDA.Core.Backend.CodeGenerator.Generation.Models;

internal static class WriteRepositoryNaming
{
    private static readonly HashSet<string> s_repositoryNamespaces =
    [
        ModuleLayer.Domain, ModuleLayer.Application, ModuleLayer.Infrastructure,
        "Persistence", "Core", "DependencyInjection", "Features", "Read", "Write"
    ];

    private static readonly HashSet<string?> s_sdkUsingNamespaces =
    [
        "System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http",
        "System.Threading", "System.Threading.Tasks"
    ];

    private static readonly FrozenSet<string> s_sdkTypeNames = new[]
        {
            typeof(object), typeof(Console), typeof(Uri), typeof(FileSystemWatcher), typeof(Enumerable),
            typeof(Queryable), typeof(HttpClient), typeof(Barrier), typeof(Parallel)
        }
        .Select(type => type.Assembly)
        .Distinct()
        .SelectMany(assembly => assembly.ExportedTypes)
        .Where(type => !type.IsNested && s_sdkUsingNamespaces.Contains(type.Namespace))
        .Select(type => type.Name)
        .ToFrozenSet(StringComparer.Ordinal);

    internal static bool RequiresAggregateAlias(string module, string feature, string aggregate)
    {
        return aggregate == feature
            || module.Split('.').Contains(aggregate, StringComparer.Ordinal)
            || s_repositoryNamespaces.Contains(aggregate)
            || s_sdkTypeNames.Contains(aggregate);
    }
}
