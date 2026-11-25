using System.Reflection;

namespace PANiXiDA.Core.Backend.CodeGenerator.Generation;

internal static class DomainTypeInspector
{
    internal const string AggregateRootType = "PANiXiDA.Core.Domain.AggregateRoots.AggregateRoot`1";
    internal const string EntityType = "PANiXiDA.Core.Domain.Entities.Entity`1";
    internal const string CreateMethodName = "Create";
    internal const string IdPropertyName = "Id";
    internal const string ValuePropertyName = "Value";

    private const string EntityInterface = "PANiXiDA.Core.Domain.Entities.IEntity";
    private const string ResultType = "PANiXiDA.Core.ResultPattern.Result";
    private const string GenericResultType = ResultType + "`1";

    internal static Type GetAggregate(Assembly domainAssembly, ResourceTarget target)
    {
        var aggregate = domainAssembly.GetType(target.Aggregate);
        if (aggregate is not { IsPublic: true, IsClass: true, IsAbstract: false, IsGenericType: false })
        {
            throw new InvalidOperationException(
                $"'{target.Aggregate}' must be a public concrete aggregate in '{target.Project.ProjectFile}'.");
        }

        return aggregate;
    }

    internal static Type GetId(Type entity, string baseTypeName)
    {
        var baseType = entity.BaseType!;
        if (!baseType.IsGenericType
            || baseType.GetGenericTypeDefinition().FullName != baseTypeName)
        {
            var baseName = baseTypeName == AggregateRootType ? "AggregateRoot" : "Entity";
            throw new InvalidOperationException($"'{entity.FullName}' must derive from {baseName}<TId>.");
        }

        var id = baseType.GetGenericArguments()[0];
        if (id.Namespace != entity.Namespace
            || id.GetProperty(ValuePropertyName)?.PropertyType != typeof(Guid)
            || !HasFactory(id, typeof(Guid), allowNullableResult: false))
        {
            throw new InvalidOperationException(
                $"'{entity.FullName}' requires a typed Guid ID in the same namespace with Value and Create(Guid).");
        }

        return id;
    }

    internal static bool IsEntity(Type type) => type.GetInterface(EntityInterface) is not null;

    internal static Type[] GetEntityElements(Type type) =>
        [.. type.GetInterfaces().Prepend(type)
            .Where(candidate => candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .Select(candidate => candidate.GetGenericArguments()[0])
            .Where(IsEntity)
            .Distinct()];

    internal static MethodInfo GetOperation(Type aggregate, string name, bool isStatic, Type? payload)
    {
        var flags = BindingFlags.Public | BindingFlags.DeclaredOnly;
        flags |= isStatic ? BindingFlags.Static : BindingFlags.Instance;
        var methods = aggregate.GetMethods(flags)
            .Where(method => method.Name == name
                && !method.IsGenericMethod
                && MatchesResult(method.ReturnType, payload))
            .ToArray();
        return methods.Length == 1
            ? methods[0]
            : throw new InvalidOperationException(
                $"'{aggregate.FullName}' requires exactly one public '{name}' method with the expected Result type.");
    }

    internal static bool HasFactory(Type type, Type argumentType, bool allowNullableResult)
    {
        var factory = type.GetMethod(CreateMethodName, BindingFlags.Public | BindingFlags.Static, [argumentType]);
        var resultType = factory?.ReturnType;
        if (resultType is null || !IsGenericResult(resultType))
        {
            return false;
        }

        var payload = resultType.GetGenericArguments()[0];
        return payload == type || (allowNullableResult && Nullable.GetUnderlyingType(payload) == type);
    }

    internal static bool MatchesResult(Type type, Type? payload) =>
        payload is null
            ? type.FullName == ResultType
            : IsGenericResult(type) && type.GetGenericArguments()[0] == payload;

    private static bool IsGenericResult(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition().FullName == GenericResultType;
}
