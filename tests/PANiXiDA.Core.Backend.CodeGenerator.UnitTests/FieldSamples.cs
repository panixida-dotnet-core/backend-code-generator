using PANiXiDA.Core.ResultPattern;

namespace PANiXiDA.Core.Backend.CodeGenerator.UnitTests;

internal sealed class FieldSamples
{
    public int Count { get; init; }

    public int? NullableCount { get; init; }


    public PrivateValue PrivateValue { get; init; } = default!;

    public GenericValue<int> GenericValue { get; init; } = default!;

    public WriteOnlyValue WriteOnlyValue { get; init; } = default!;

    public PrivateGetterValue PrivateGetterValue { get; init; } = default!;

    public StaticValue StaticValue { get; init; } = default!;

    public NonScalarValue NonScalarValue { get; init; } = default!;

    public MissingFactory MissingFactory { get; init; } = default!;

    public WrongFactory WrongFactory { get; init; } = default!;

    public WrongGenericFactory WrongGenericFactory { get; init; } = default!;

    public WrongPayload? WrongPayload { get; init; }

    public WrongPayload RequiredWrongPayload { get; init; } = default!;

    public NonIntegerLength NonIntegerLength { get; init; } = default!;

    public NonLiteralLength? NonLiteralLength { get; init; }

    public NullableQuantity? NullableQuantity { get; init; }

    public CompoundStruct? CompoundStruct { get; init; }

    public UnboundComponent UnboundComponent { get; init; } = default!;

    public WrongComponentType WrongComponentType { get; init; } = default!;

    public WrongComponentNullability WrongComponentNullability { get; init; } = default!;

    public UnsupportedComponent UnsupportedComponent { get; init; } = default!;

    public DuplicateComponents DuplicateComponents { get; init; } = default!;

    public AmbiguousFactory AmbiguousFactory { get; init; } = default!;

    public void WrongName(int missing) => _ = missing;

    public void WrongScalarType(object count) => _ = count;

    public void WrongScalarNullability(int? count) => _ = count;

    public void DuplicateBinding(int count, int Count) => _ = count + Count;

    public void WrongCompoundType(string? compoundStruct) => _ = compoundStruct;

    public void WrongCompoundNullability(CompoundStruct compoundStruct) => _ = compoundStruct;

    public void WrongLeadingId(string id) => _ = id;

    public void NoParameters()
    {
    }
}

internal sealed class PrivateValue;

public sealed class GenericValue<T>;

public sealed class WriteOnlyValue { public int Value { set => _ = value; } }

public sealed class PrivateGetterValue { public int Value { private get; set; } }

public sealed class StaticValue { public static int Value => 0; }

public sealed class NonScalarValue { public object Value { get; } = new(); }

public sealed record MissingFactory(int Value);

public sealed record WrongFactory(int Value) { public static WrongFactory Create(int value) => new(value); }

public sealed record WrongGenericFactory(int Value) { public static List<int> Create(int value) => [value]; }

public sealed record WrongPayload(int Value) { public static Result<int> Create(int value) => Result.Success(value); }

public sealed record NonIntegerLength(string Value)
{
    public const string MaxLength = "ten";

    public static Result<NonIntegerLength> Create(string value) => Result.Success(new NonIntegerLength(value));
}

public sealed record NonLiteralLength(string Value)
{
    public static readonly int MaxLength = 10;

    public static Result<NonLiteralLength> Create(string? value) => Result.Success(new NonLiteralLength(value ?? ""));
}

public readonly record struct NullableQuantity(int Value)
{
    public static Result<NullableQuantity?> Create(int? value) =>
        Result.Success<NullableQuantity?>(value.HasValue ? new NullableQuantity(value.Value) : null);
}

public readonly record struct CompoundStruct(int? Count, string? Text)
{
    public int Hidden { private get; init; }

    public int this[int index] => index;

    public static Result<CompoundStruct> Create(int? count, string? text) => Result.Success(new CompoundStruct(count, text));

    public static Result<CompoundStruct> Create(int? count) => Create(count, null);

    public static Result<CompoundStruct> Create<T>(int? count) => Create(count, null);

    public static CompoundStruct Create(string? text, int? count) => new(count, text);
}

public sealed record UnboundComponent(int Count, string Text)
{
    public static Result<UnboundComponent> Create(int missing, string text) => Result.Success(new UnboundComponent(missing, text));
}

public sealed record WrongComponentType(int Count, string Text)
{
    public static Result<WrongComponentType> Create(string count, string text) => Result.Success(new WrongComponentType(count.Length, text));
}

public sealed record WrongComponentNullability(int Count, string? Text)
{
    public static Result<WrongComponentNullability> Create(int count, string text) => Result.Success(new WrongComponentNullability(count, text));
}

public sealed record UnsupportedComponent(int Count, object Value)
{
    public static Result<UnsupportedComponent> Create(int count, object value) => Result.Success(new UnsupportedComponent(count, value));
}

public sealed record DuplicateComponents(int Count)
{
    public static Result<DuplicateComponents> Create(int count, int Count) => Result.Success(new DuplicateComponents(count + Count));
}

public sealed record AmbiguousFactory(int Count, string Text)
{
    public static Result<AmbiguousFactory> Create(int count, string text) => Result.Success(new AmbiguousFactory(count, text));

    public static Result<AmbiguousFactory> Create(string text, int count) => Create(count, text);
}
