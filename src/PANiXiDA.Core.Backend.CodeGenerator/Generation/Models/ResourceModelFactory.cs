using System.Reflection;
using System.Text.RegularExpressions;

using PANiXiDA.Core.Backend.CodeGenerator.Configuration;
using PANiXiDA.Core.Backend.CodeGenerator.Generation.Domain;

namespace PANiXiDA.Core.Backend.CodeGenerator.Generation.Models;

internal static class ResourceModelFactory
{
    internal static ResourceModel Create(Assembly domainAssembly, ResourceTarget target, ResourceSettings settings)
    {
        if (!IsRoute(settings.Route))
        {
            throw new InvalidOperationException(
                $"'{target.Aggregate}' requires a lowercase, single-segment route.");
        }

        var aggregate = DomainTypeInspector.GetAggregate(domainAssembly, target);
        var id = DomainTypeInspector.GetId(aggregate, DomainTypeInspector.AggregateRootType);
        var nullability = new NullabilityInfoContext();
        var fields = CodeFieldFactory.GetFields(aggregate, nullability);
        var create = DomainTypeInspector.GetOperation(
            aggregate, DomainTypeInspector.CreateMethodName, isStatic: true, payload: aggregate);
        var update = DomainTypeInspector.GetOperation(aggregate, "Update", isStatic: false, payload: null);

        return MakeModel(
            target,
            id,
            settings.Route,
            (fields,
                CodeFieldFactory.BindParameters(create, fields, nullability),
                CodeFieldFactory.BindParameters(update, fields, nullability)),
            (settings.DetailsFields, settings.ListFields));
    }

    internal static ResourceModel CreateChild(
        Assembly domainAssembly,
        ResourceTarget parent,
        ResourceTarget target,
        string parentRoute,
        ChildSettings settings,
        IReadOnlyList<string> artifacts)
    {
        if (!IsRoute(parentRoute) || !IsRoute(settings.Route))
        {
            throw new InvalidOperationException(
                $"'{parent.Aggregate}' and its child require lowercase route segments.");
        }

        var aggregate = DomainTypeInspector.GetAggregate(domainAssembly, parent);
        var parentId = DomainTypeInspector.GetId(aggregate, DomainTypeInspector.AggregateRootType);
        var collection = aggregate.GetProperty(settings.Collection, BindingFlags.Instance | BindingFlags.Public);
        var entities = collection is { GetMethod.IsPublic: true }
            ? DomainTypeInspector.GetEntityElements(collection.PropertyType)
            : [];
        if (entities is not [{ IsPublic: true, IsClass: true, IsAbstract: false, IsGenericType: false } entity])
        {
            throw new InvalidOperationException(
                $"'{parent.Aggregate}.{settings.Collection}' must be a public collection of child entities.");
        }

        var id = DomainTypeInspector.GetId(entity, DomainTypeInspector.EntityType);
        var nullability = new NullabilityInfoContext();
        var fields = CodeFieldFactory.GetFields(entity, nullability);
        if (fields.Any(field => field.Name == "ParentId"))
        {
            throw new InvalidOperationException(
                $"'{entity.FullName}.ParentId' is reserved for the generated association.");
        }

        var create = GetChildOperation(aggregate, settings.CreateMethod, id, artifacts, "ChildCreate");
        var update = GetChildOperation(aggregate, settings.UpdateMethod, null, artifacts, "ChildUpdate");
        var delete = GetChildOperation(aggregate, settings.DeleteMethod, null, artifacts, "ChildDelete");
        var createFields = create is null ? [] : CodeFieldFactory.BindParameters(create, fields, nullability);
        var updateFields = update is null ? [] : CodeFieldFactory.BindParameters(update, fields, nullability, id);
        if (delete is not null)
        {
            CodeFieldFactory.BindParameters(delete, [], nullability, id);
        }

        var routeId = char.ToLowerInvariant(parent.Resource[0]) + parent.Resource[1..] + "Id";
        var child = new ChildModel(
            parent.Domain,
            parent.Feature,
            parent.Resource,
            parentId.Name,
            routeId,
            settings.Collection,
            entity.Name,
            settings.CreateMethod,
            settings.UpdateMethod,
            settings.DeleteMethod);
        return MakeModel(
            target,
            id,
            $"{parentRoute}/{{{routeId}:guid}}/{settings.Route}",
            (fields, createFields, updateFields),
            (settings.DetailsFields, settings.ListFields),
            child);
    }

    private static ResourceModel MakeModel(
        ResourceTarget target,
        Type id,
        string route,
        (CodeField[] All, CodeField[] Create, CodeField[] Update) fields,
        (IReadOnlyList<string> Details, IReadOnlyList<string> List) projections,
        ChildModel? child = null)
    {
        var readableFields = fields.All
            .Prepend(new CodeField(DomainTypeInspector.IdPropertyName, nameof(Guid), false, null, null, false))
            .ToArray();

        return new ResourceModel(
            target.Project.Module,
            target.Project.ModuleName,
            target.Feature,
            target.Resource,
            id.Namespace!,
            id.Name,
            route,
            fields.All,
            fields.Create,
            fields.Update,
            CodeFieldFactory.SelectProjection(projections.Details, readableFields, "detailsFields"),
            CodeFieldFactory.SelectProjection(projections.List, readableFields, "listFields"),
            child);
    }

    private static MethodInfo? GetChildOperation(
        Type aggregate, string? name, Type? payload, IReadOnlyList<string> artifacts, string operation)
    {
        if (!artifacts.Any(artifact => artifact.StartsWith(operation, StringComparison.Ordinal)))
        {
            return null;
        }

        if (!DomainProject.IsIdentifier(name))
        {
            throw new InvalidOperationException($"'{aggregate.FullName}' requires a method for '{operation}'.");
        }

        return DomainTypeInspector.GetOperation(aggregate, name!, isStatic: false, payload);
    }

    private static bool IsRoute(string? route) =>
        Regex.IsMatch(
            route ?? string.Empty,
            @"^[a-z][a-z0-9]*(?:-[a-z0-9]+)*\z",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
}
