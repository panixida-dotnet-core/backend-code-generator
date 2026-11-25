using System.Reflection;

using PANiXiDA.Core.Backend.CodeGenerator.Generation.Domain;

namespace PANiXiDA.Core.Backend.CodeGenerator.Generation.Models;

internal static class CodeFieldFactory
{
    private static readonly Dictionary<Type, string> s_scalarNames = new()
    {
        [typeof(bool)] = "bool",
        [typeof(byte)] = "byte",
        [typeof(short)] = "short",
        [typeof(int)] = "int",
        [typeof(long)] = "long",
        [typeof(float)] = "float",
        [typeof(double)] = "double",
        [typeof(decimal)] = "decimal",
        [typeof(string)] = "string",
        [typeof(Guid)] = nameof(Guid),
        [typeof(DateOnly)] = nameof(DateOnly),
        [typeof(TimeOnly)] = nameof(TimeOnly),
        [typeof(DateTime)] = nameof(DateTime),
        [typeof(DateTimeOffset)] = nameof(DateTimeOffset)
    };

    internal static CodeField[] GetFields(Type entity, NullabilityInfoContext nullability) =>
        [.. entity.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(property => property.Name != DomainTypeInspector.IdPropertyName
                && property.GetMethod is { IsPublic: true }
                && property.GetIndexParameters().Length == 0
                && property.SetMethod is not null
                && !DomainTypeInspector.IsEntity(property.PropertyType)
                && DomainTypeInspector.GetEntityElements(property.PropertyType).Length == 0)
            .Select(property => DescribeField(property, nullability))];

    internal static CodeField DescribeField(PropertyInfo property, NullabilityInfoContext nullability)
    {
        var declaredType = property.PropertyType;
        var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        var optional = IsOptional(declaredType, nullability.Create(property));
        if (s_scalarNames.TryGetValue(type, out var scalar))
        {
            return new CodeField(property.Name, optional ? scalar + "?" : scalar, optional, null, null, false);
        }

        var singleValue = DescribeSingleValue(property.Name, type, optional);
        if (singleValue is not null)
        {
            return singleValue;
        }

        var propertyName = $"{property.DeclaringType!.FullName}.{property.Name}";
        var components = GetComponents(type, nullability, propertyName);
        if (components is not null)
        {
            return new CodeField(
                property.Name,
                string.Empty,
                optional,
                $"global::{type.FullName}",
                null,
                false,
                components,
                optional && type.IsValueType);
        }

        throw new InvalidOperationException(
            $"'{propertyName}' requires a scalar or a value object with a matching Create factory.");
    }

    internal static CodeField[] BindParameters(
        MethodInfo method, IReadOnlyList<CodeField> fields, NullabilityInfoContext nullability, Type? leadingId = null)
    {
        var parameters = method.GetParameters();
        if (leadingId is not null)
        {
            if (parameters.Length == 0 || parameters[0].ParameterType != leadingId)
            {
                throw new InvalidOperationException($"'{method.Name}' must take '{leadingId.Name}' first.");
            }

            parameters = parameters[1..];
        }

        var bound = new List<CodeField>(parameters.Length);
        foreach (var parameter in parameters)
        {
            var field = fields.SingleOrDefault(
                item => string.Equals(item.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
            var underlying = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
            var optional = IsOptional(parameter.ParameterType, nullability.Create(parameter));
            if (field is null
                || !MatchesParameter(field, underlying, optional)
                || bound.Contains(field))
            {
                var parameterName = $"{method.DeclaringType!.Name}.{method.Name}({parameter.Name})";
                throw new InvalidOperationException(
                    $"'{parameterName}' must match a writable domain property and its input type.");
            }

            bound.Add(field);
        }

        return [.. bound];
    }

    internal static CodeField[] SelectProjection(
        IReadOnlyList<string>? names, IReadOnlyList<CodeField> fields, string setting)
    {
        if (names is null || !names.Contains(DomainTypeInspector.IdPropertyName)
            || names.Distinct(StringComparer.Ordinal).Count() != names.Count)
        {
            throw new InvalidOperationException(
                $"'{setting}' must contain {DomainTypeInspector.IdPropertyName} and distinct property names.");
        }

        return [.. names.Select(name => fields.SingleOrDefault(field => field.Name == name)
            ?? throw new InvalidOperationException($"Unknown property '{name}' in '{setting}'."))];
    }

    private static CodeField? DescribeSingleValue(string name, Type type, bool optional)
    {
        var value = type.GetProperty(DomainTypeInspector.ValuePropertyName) ?? type.GetProperty("Name");
        if (!type.IsPublic
            || type.IsGenericType
            || value?.GetMethod is not { IsPublic: true, IsStatic: false }
            || !s_scalarNames.TryGetValue(value.PropertyType, out var scalar))
        {
            return null;
        }

        var factoryInput = optional && value.PropertyType.IsValueType
            ? typeof(Nullable<>).MakeGenericType(value.PropertyType)
            : value.PropertyType;
        if (!DomainTypeInspector.HasFactory(type, factoryInput, allowNullableResult: optional))
        {
            return null;
        }

        var maxLength = type.GetField("MaxLength", BindingFlags.Public | BindingFlags.Static);
        var valueMember = optional && type.IsValueType ? $"Value.{value.Name}" : value.Name;
        return new CodeField(
            name,
            optional ? scalar + "?" : scalar,
            optional,
            $"global::{type.FullName}",
            valueMember,
            maxLength is { IsLiteral: true } && maxLength.FieldType == typeof(int));
    }

    private static ValueComponent[]? GetComponents(Type type, NullabilityInfoContext nullability, string propertyName)
    {
        if (!type.IsPublic || type.IsGenericType)
        {
            return null;
        }

        var candidates = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == DomainTypeInspector.CreateMethodName
                && !method.IsGenericMethod
                && DomainTypeInspector.MatchesResult(method.ReturnType, type))
            .Select(method => method.GetParameters())
            .Where(parameters => parameters.Length > 1)
            .ToArray();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is { IsPublic: true } && property.GetIndexParameters().Length == 0)
            .ToArray();
        var matches = candidates
            .Select(parameters => BindComponents(parameters, properties, nullability))
            .OfType<ValueComponent[]>()
            .ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"'{propertyName}' has more than one matching compound Create factory.");
        }

        return matches.FirstOrDefault();
    }

    private static ValueComponent[]? BindComponents(
        ParameterInfo[] parameters,
        IReadOnlyList<PropertyInfo> properties,
        NullabilityInfoContext nullability)
    {
        var components = new List<ValueComponent>(parameters.Length);
        foreach (var parameter in parameters)
        {
            var property = properties.SingleOrDefault(candidate =>
                string.Equals(candidate.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
            var optional = IsOptional(parameter.ParameterType, nullability.Create(parameter));
            var underlying = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
            if (property is null
                || property.PropertyType != parameter.ParameterType
                || optional != IsOptional(property.PropertyType, nullability.Create(property))
                || !s_scalarNames.TryGetValue(underlying, out var scalar)
                || components.Any(component => component.Name == property.Name))
            {
                return null;
            }

            components.Add(new ValueComponent(property.Name, optional ? scalar + "?" : scalar));
        }

        return [.. components];
    }

    private static bool MatchesParameter(CodeField field, Type parameterType, bool optional)
    {
        if (field.Composite)
        {
            return field.Optional == optional && field.ValueObject == $"global::{parameterType.FullName}";
        }

        return s_scalarNames.TryGetValue(parameterType, out var scalar)
            && field.Type == (optional ? scalar + "?" : scalar);
    }

    private static bool IsOptional(Type type, NullabilityInfo nullability) =>
        Nullable.GetUnderlyingType(type) is not null || nullability.ReadState == NullabilityState.Nullable;
}
