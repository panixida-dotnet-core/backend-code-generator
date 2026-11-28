namespace PANiXiDA.Core.Backend.CodeGenerator.FileSystem;

internal sealed record RepositoryLayout(string Root, string TemplateBaseDirectory)
{
    internal string ResolvePath(string path, string relativeTo) => ResolvePath(path, relativeTo, Root);

    internal static string ResolvePath(string path, string relativeTo, string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path, relativeTo);
        var relativePath = Path.GetRelativePath(root, fullPath);
        ValidateRelativePath(path, relativePath);
        if (relativePath != ".")
        {
            EnsureNoLinks(fullPath, relativePath);
        }

        return fullPath;
    }

    internal static void ValidateRelativePath(string path, string relativePath)
    {
        if (relativePath == ".."
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException($"'{path}' must resolve inside the repository.");
        }
    }

    private static void EnsureNoLinks(string fullPath, string relativePath)
    {
        var remaining = relativePath;
        var currentPath = fullPath;
        while (remaining.Length > 0)
        {
            if ((File.Exists(currentPath) || Directory.Exists(currentPath))
                && File.GetAttributes(currentPath).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException($"'{currentPath}' points to a symbolic link or junction.");
            }

            currentPath = Path.GetDirectoryName(currentPath)!;
            remaining = Path.GetDirectoryName(remaining)!;
        }
    }
}
