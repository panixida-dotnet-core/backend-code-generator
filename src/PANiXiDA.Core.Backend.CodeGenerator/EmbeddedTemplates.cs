using System.Reflection;

namespace PANiXiDA.Core.Backend.CodeGenerator;

internal static class EmbeddedTemplates
{
    private const string Prefix = "Templates/";
    private static readonly Assembly s_assembly = typeof(EmbeddedTemplates).Assembly;

    private static readonly Dictionary<string, string> s_resources = s_assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal))
        .ToDictionary(name => name[Prefix.Length..].Replace('\\', '/'), StringComparer.Ordinal);

    internal static IEnumerable<string> Paths => s_resources.Keys;

    internal static bool Contains(string path) => s_resources.ContainsKey(path);

    internal static Stream Open(string path)
    {
        if (s_resources.TryGetValue(path, out var name) && s_assembly.GetManifestResourceStream(name) is { } stream)
        {
            return stream;
        }

        throw new InvalidOperationException($"Bundled template '{path}' was not found.");
    }

    internal static string Read(string path)
    {
        using var reader = new StreamReader(Open(path));
        return reader.ReadToEnd();
    }
}
