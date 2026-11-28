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
        var components = GetComponents(type, nullability, propertyName, optional);
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
            var bindsDomainValue = field?.ValueObject == $"global::{underlying.FullName}";
            if (field is null
                || field.Optional != optional
                || (!bindsDomainValue && (!s_scalarNames.TryGetValue(underlying, out var scalar)
                    || field.Type != (optional ? scalar + "?" : scalar)))
                || bound.Any(item => item.Name == field.Name))
            {
                var parameterName = $"{method.DeclaringType!.Name}.{method.Name}({parameter.Name})";
                throw new InvalidOperationException(
                    $"'{parameterName}' must match a writable domain property and its input type.");
            }

            var binding = field with
            {
                ParameterName = parameter.Name,
                BindsDomainValue = bindsDomainValue
            };
            bound.Add(bindsDomainValue && optional ? BindNullableFactory(binding, underlying, nullability) : binding);
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
        var value = GetValueProperty(type);
        if (!type.IsPublic
            || type.IsGenericType
            || value?.GetMethod is not { IsPublic: true, IsStatic: false }
            || !s_scalarNames.TryGetValue(value.PropertyType, out var scalar))
        {
            return null;
        }

        var factoryInput = optional ? ToNullableType(value.PropertyType) : value.PropertyType;
        if (DomainTypeInspector.FindFactory(type, [factoryInput], allowNullableResult: optional) is null)
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

    private static ValueComponent[]? GetComponents(Type type, NullabilityInfoContext nullability, string propertyName, bool optional)
    {
        if (!type.IsPublic || type.IsGenericType)
        {
            return null;
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is { IsPublic: true } && property.GetIndexParameters().Length == 0)
            .ToArray();
        var matches = GetCompoundFactories(type, allowNullableResult: optional)
            .Select(parameters => BindComponents(parameters, properties, nullability, nullableInput: false))
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
        NullabilityInfoContext nullability,
        bool nullableInput)
    {
        var components = new List<ValueComponent>(parameters.Length);
        foreach (var parameter in parameters)
        {
            var property = properties.SingleOrDefault(candidate =>
                string.Equals(candidate.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
            var underlying = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
            if (property is null
                || !MatchesComponentType(property, parameter, nullability, nullableInput)
                || !s_scalarNames.TryGetValue(underlying, out var scalar)
                || components.Any(component => component.Name == property.Name))
            {
                return null;
            }

            var optional = IsOptional(property.PropertyType, nullability.Create(property));
            components.Add(new ValueComponent(property.Name, optional ? scalar + "?" : scalar, parameter.Name!));
        }

        return [.. components];
    }

    private static CodeField BindNullableFactory(CodeField field, Type type, NullabilityInfoContext nullability)
    {
        if (field.Components is not { } components)
        {
            var value = GetValueProperty(type)!;
            var factory = DomainTypeInspector.FindFactory(type, [ToNullableType(value.PropertyType)], allowNullableResult: true)!;
            if (nullability.Create(factory.GetParameters()[0]).WriteState == NullabilityState.NotNull)
            {
                throw new InvalidOperationException($"Optional value object '{field.Name}' requires Create accepting a nullable value.");
            }

            return field;
        }

        var properties = components.Select(component => type.GetProperty(component.Name)!).ToArray();
        var matches = GetCompoundFactories(type, allowNullableResult: true)
            .Where(parameters => parameters.Length == components.Count)
            .Select(parameters => BindComponents(parameters, properties, nullability, nullableInput: true))
            .OfType<ValueComponent[]>()
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"Optional value object '{field.Name}' requires exactly one Create accepting its nullable components.");
        }

        return field with { NullableFactoryComponents = matches[0] };
    }

    private static IEnumerable<ParameterInfo[]> GetCompoundFactories(Type type, bool allowNullableResult) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == DomainTypeInspector.CreateMethodName
                && !method.IsGenericMethod
                && DomainTypeInspector.MatchesResult(method.ReturnType, type, allowNullableResult))
            .Select(method => method.GetParameters())
            .Where(parameters => parameters.Length > 1);

    private static bool MatchesComponentType(
        PropertyInfo property, ParameterInfo parameter, NullabilityInfoContext nullability, bool nullableInput)
    {
        if (nullableInput)
        {
            return parameter.ParameterType == ToNullableType(property.PropertyType)
                && nullability.Create(parameter).WriteState != NullabilityState.NotNull;
        }

        return property.PropertyType == parameter.ParameterType
            && (!IsOptional(property.PropertyType, nullability.Create(property))
                || nullability.Create(parameter).WriteState != NullabilityState.NotNull);
    }

    private static PropertyInfo? GetValueProperty(Type type) =>
        type.GetProperty(DomainTypeInspector.ValuePropertyName) ?? type.GetProperty("Name");

    private static Type ToNullableType(Type type) =>
        type.IsValueType && Nullable.GetUnderlyingType(type) is null ? typeof(Nullable<>).MakeGenericType(type) : type;

    private static bool IsOptional(Type type, NullabilityInfo nullability) =>
        Nullable.GetUnderlyingType(type) is not null || nullability.ReadState == NullabilityState.Nullable;
}
