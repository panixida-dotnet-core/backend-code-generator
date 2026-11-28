using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Backend.CodeGenerator.Configuration;
using PANiXiDA.Core.Backend.CodeGenerator.FileSystem;

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

        var root = ResolveRepositoryRoot(repositoryRoot);
        var configurationFile = RepositoryLayout.ResolvePath(configurationPath, root, root);
        var repository = new RepositoryLayout(root, Path.GetDirectoryName(configurationFile)!);
        await using var configuration = File.OpenRead(configurationFile);
        var settings = await JsonSerializer.DeserializeAsync<GeneratorSettings>(
            configuration, s_jsonOptions, cancellationToken);

        return await GenerateJobsAsync(repository, settings?.Jobs, cancellationToken);
    }

    /// <summary>
    /// Generates files using the jobs in the supplied configuration.
    /// </summary>
    /// <param name="configuration">The configuration containing a top-level jobs section.</param>
    /// <param name="repositoryRoot">The backend repository root. Defaults to the current working directory.</param>
    /// <param name="templateBaseDirectory">
    /// The base directory for relative template overrides, within the repository. Defaults to the repository root.
    /// </param>
    /// <param name="cancellationToken">The token used to cancel work before applying file changes.</param>
    /// <returns>The paths and actions of files affected by the jobs.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
    /// <exception cref="ArgumentException">A supplied path is empty or unsupported.</exception>
    /// <exception cref="DirectoryNotFoundException">The repository root does not exist.</exception>
    /// <exception cref="InvalidOperationException">Configuration or a requested generation job is invalid.</exception>
    /// <exception cref="IOException">The generator cannot read or write a requested file.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled before applying changes.</exception>
    public static async Task<IReadOnlyList<GeneratedFile>> GenerateAsync(
        IConfiguration configuration,
        string? repositoryRoot = null,
        string? templateBaseDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        var root = ResolveRepositoryRoot(repositoryRoot ?? Directory.GetCurrentDirectory());
        var templateDirectory = templateBaseDirectory is null
            ? root
            : RepositoryLayout.ResolvePath(templateBaseDirectory, root, root);
        var repository = new RepositoryLayout(root, templateDirectory);
        var jobs = configuration.GetSection("jobs")
            .Get<IReadOnlyList<JobSettings>>(options => options.ErrorOnUnknownConfiguration = true);

        return await GenerateJobsAsync(repository, jobs, cancellationToken);
    }

    private static string ResolveRepositoryRoot(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Repository directory '{root}' was not found.");
        }

        return root;
    }

    private static async Task<IReadOnlyList<GeneratedFile>> GenerateJobsAsync(
        RepositoryLayout repository,
        IReadOnlyList<JobSettings>? jobs,
        CancellationToken cancellationToken)
    {
        if (jobs is not { Count: > 0 })
        {
            return [];
        }

        var changes = await Generation.CodeGenerator.RunJobsAsync(repository, jobs, cancellationToken);
        return [.. changes.Select(change => new GeneratedFile(
            Path.GetRelativePath(repository.Root, change.Path), change.Action))];
    }
}
