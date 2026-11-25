namespace PANiXiDA.Core.Backend.CodeGenerator.Generation.Models;

internal sealed record ChildModel(
    string ParentDomain,
    string ParentFeature,
    string ParentResource,
    string ParentId,
    string RouteId,
    string Collection,
    string Entity,
    string? CreateMethod,
    string? UpdateMethod,
    string? DeleteMethod);
