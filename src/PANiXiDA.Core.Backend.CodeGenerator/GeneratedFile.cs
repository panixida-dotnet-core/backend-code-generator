namespace PANiXiDA.Core.Backend.CodeGenerator;

/// <summary>
/// A file affected by a generator job.
/// </summary>
/// <param name="Path">The file path relative to the repository root.</param>
/// <param name="Action">The change applied to the file.</param>
public sealed record GeneratedFile(string Path, GeneratedFileAction Action);
