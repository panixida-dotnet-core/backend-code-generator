using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.RegularExpressions;

using PANiXiDA.Core.Backend.CodeGenerator.FileSystem;

namespace PANiXiDA.Core.Backend.CodeGenerator.Generation;

internal sealed record DomainProject(string ProjectFile, string Module, string ModuleName)
{
    private const string DomainSuffix = "." + ModuleLayer.Domain;
    private const string ProjectFileSuffix = DomainSuffix + ".csproj";

    internal static DomainProject Find(RepositoryLayout repository, string pathToModels)
    {
        var path = repository.ResolvePath(pathToModels, repository.Root);
        var projects = Directory.Exists(path)
            ? Directory.GetFiles(path, "*" + ProjectFileSuffix)
            : [path];
        if (projects.Length != 1
            || !projects[0].EndsWith(ProjectFileSuffix, StringComparison.Ordinal)
            || !File.Exists(projects[0]))
        {
            throw new InvalidOperationException($"'{pathToModels}' must identify exactly one {ProjectFileSuffix}.");
        }

        var projectFile = repository.ResolvePath(projects[0], repository.Root);
        var module = Path.GetFileNameWithoutExtension(projectFile)[..^DomainSuffix.Length];
        var moduleName = module[(module.LastIndexOf('.') + 1)..];
        return new DomainProject(projectFile, module, moduleName);
    }

    internal ResourceTarget Locate(string aggregate)
    {
        var prefix = $"{Module}{DomainSuffix}.";
        if (!string.IsNullOrWhiteSpace(aggregate) && aggregate.StartsWith(prefix, StringComparison.Ordinal))
        {
            var names = aggregate[prefix.Length..].Split('.');
            if (names.Length == 2 && names.All(IsIdentifier))
            {
                return new ResourceTarget(this, names[0], names[1]);
            }
        }

        throw new InvalidOperationException($"'{aggregate}' must match '{prefix}<Feature>.<Aggregate>'.");
    }

    internal async Task<Assembly> BuildAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveDotnetHost(
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"),
                RuntimeEnvironment.GetRuntimeDirectory(),
                OperatingSystem.IsWindows()),
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList =
            {
                "build",
                ProjectFile,
                "--nologo",
                "--verbosity",
                "quiet",
                "-target:Build",
                "-getProperty:TargetPath",
                "-p:CopyLocalLockFileAssemblies=true"
            }
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await StopBuildAsync(process);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var standardOutput = await output;
        var standardError = await errors;
        if (process.ExitCode != 0)
        {
            var buildOutput = standardOutput + standardError;
            throw new InvalidOperationException($"Could not build '{ProjectFile}'.{Environment.NewLine}{buildOutput}");
        }

        return LoadAssembly(FindTargetPath(standardOutput));
    }

    internal static string ResolveDotnetHost(string? configuredPath, string runtimeDirectory, bool isWindows)
    {
        if (Path.IsPathFullyQualified(configuredPath.AsSpan()))
        {
            return configuredPath!;
        }

        var fileName = isWindows ? "dotnet.exe" : "dotnet";
        return Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", "..", fileName));
    }

    internal static async Task StopBuildAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    internal string FindTargetPath(string standardOutput)
    {
        const StringSplitOptions LineOptions = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;
        var targetPath = standardOutput.Split('\n', LineOptions)
            .LastOrDefault();
        if (targetPath is null || !Path.IsPathFullyQualified(targetPath) || !File.Exists(targetPath))
        {
            throw new InvalidOperationException($"Could not locate the build output of '{ProjectFile}'.");
        }

        return targetPath;
    }

    internal static Assembly LoadAssembly(string targetPath)
    {
        var context = new DomainLoadContext(targetPath);
        try
        {
            return context.LoadFile(targetPath);
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    internal static bool IsIdentifier(string? name) =>
        Regex.IsMatch(
            name ?? string.Empty,
            @"^[A-Za-z_][A-Za-z0-9_]*\z",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private sealed class DomainLoadContext(string targetPath) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(targetPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var dependency = _resolver.ResolveAssemblyToPath(assemblyName);
            return dependency is null ? null : LoadFile(dependency);
        }

        internal Assembly LoadFile(string path)
        {
            using var file = File.OpenRead(path);
            return LoadFromStream(file);
        }
    }
}
