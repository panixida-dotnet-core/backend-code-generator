namespace PANiXiDA.Core.Backend.CodeGenerator.Generation;

internal sealed record TemplateArtifact(
    string Name, string Layer, string RelativePath, string SourcePath, bool Child);
