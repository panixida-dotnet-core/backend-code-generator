namespace PANiXiDA.Core.Backend.CodeGenerator;

/// <summary>
/// The change applied to a generated file.
/// </summary>
public enum GeneratedFileAction
{
    /// <summary>A new source file was created.</summary>
    Created,

    /// <summary>An existing source file was replaced.</summary>
    Replaced,

    /// <summary>A previously generated source file was deleted.</summary>
    Deleted
}
