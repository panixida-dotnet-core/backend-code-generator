using System.Text.Json;
using System.Text.Json.Serialization;

using PANiXiDA.Core.Backend.CodeGenerator.FileSystem;
using PANiXiDA.Core.Backend.CodeGenerator.Generation;

namespace PANiXiDA.Core.Backend.CodeGenerator;

/// <summary>
/// Generates editable CRUD source files from domain aggregates in a .NET backend repository.
/// </summary>
public static class BackendCodeGenerator
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>
    /// Reads the requested jobs and applies the resulting file changes.
    /// </summary>
    /// <param name="repositoryRoot">The root directory of the backend repository.</param>
    /// <param name="configurationPath">The path to the JSON configuration, relative to the repository root or absolute within it.</param>
    /// <param name="cancellationToken">The token used to cancel work before applying file changes.</param>
    /// <returns>The paths and actions of files affected by the jobs.</returns>
    /// <exception cref="ArgumentException">A path is empty or unsupported.</exception>
    /// <exception cref="DirectoryNotFoundException">The repository root does not exist.</exception>
    /// <exception cref="JsonException">The configuration contains invalid JSON or unsupported settings.</exception>
    /// <exception cref="InvalidOperationException">The generator cannot plan or render a requested job.</exception>
    /// <exception cref="IOException">The generator cannot read or write a requested file.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled before applying changes.</exception>
    public static async Task<IReadOnlyList<GeneratedFile>> GenerateAsync(
        string repositoryRoot,
        string configurationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        cancellationToken.ThrowIfCancellationRequested();

        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Repository directory '{root}' was not found.");
        }

        var configurationFile = RepositoryLayout.ResolvePath(configurationPath, root, root);
        var repository = new RepositoryLayout(root, Path.GetDirectoryName(configurationFile)!);
        await using var configuration = File.OpenRead(configurationFile);
        var settings = await JsonSerializer.DeserializeAsync<GeneratorSettings>(
            configuration, s_jsonOptions, cancellationToken);

        if (settings?.Jobs is not { Count: > 0 } jobs)
        {
            return [];
        }

        var changes = await Generation.CodeGenerator.RunJobsAsync(repository, jobs, cancellationToken);
        return [.. changes.Select(change => new GeneratedFile(Path.GetRelativePath(root, change.Path), change.Action))];
    }
}
