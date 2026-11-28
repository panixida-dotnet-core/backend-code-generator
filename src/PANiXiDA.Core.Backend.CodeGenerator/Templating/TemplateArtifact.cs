namespace PANiXiDA.Core.Backend.CodeGenerator.Templating;

internal sealed record TemplateArtifact(
    string Name, string Layer, string RelativePath, string SourcePath, bool Child);
