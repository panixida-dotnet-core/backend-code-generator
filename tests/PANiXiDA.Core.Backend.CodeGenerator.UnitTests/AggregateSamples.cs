using System.Collections;
using PANiXiDA.Core.Domain.AggregateRoots;
using PANiXiDA.Core.Domain.Entities;
using PANiXiDA.Core.Domain.Identifiers;
using PANiXiDA.Core.ResultPattern;

namespace Samples.Catalog.Domain.Models;

public readonly record struct SampleId(Guid Value) : IStronglyTypedId
{
    public static Result<SampleId> Create(Guid value) => Result.Success(new SampleId(value));
}

public sealed class ValidRoot : AggregateRoot<SampleId>
{
    private ValidRoot(SampleId id) : base(id)
    {
        Id = id;
    }

    public new SampleId Id { get; private set; }

    public int Count { get; private set; }

    public int ReadOnly => 0;

    public int WriteOnly { set => _ = value; }

    public int PrivateGetter { private get; set; }

    public int this[int index]
    {
        get => index;
        set => _ = value;
    }

    public EntityChild? EntityReference { get; private set; }

    public IReadOnlyList<EntityChild> Variants { get; private set; } = [];

    public IReadOnlyList<EntityChild> WriteOnlyVariants { set => _ = value; }

    public IReadOnlyList<int> Scalars => [];

    public HiddenChildren Hidden => new();

    public IReadOnlyList<ChildValue> StructChildren => [];

    public IReadOnlyList<AbstractChild> AbstractChildren => [];

    public IReadOnlyList<GenericChild<int>> GenericChildren => [];

    public IReadOnlyList<ValidRoot> AggregateChildren => [];

    public IReadOnlyList<ChildWithParentId> ReservedChildren => [];

    public static Result<ValidRoot> Create() => Result.Success(new ValidRoot(default));

    public static Result<ValidRoot> Create<T>(T value) => Create();

    public static ValidRoot Create(int count) => new(default) { Count = count };

    public Result Update() => Result.Success();

    public Result<SampleId> AddVariant() => Result.Success(default(SampleId));

    public Result UpdateVariant(SampleId id) => Result.Success();

    public Result RemoveVariant(SampleId id) => Result.Success();
}

public sealed class EntityChild(SampleId id) : Entity<SampleId>(id);

public readonly struct ChildValue : IEntity;

public abstract class AbstractChild(SampleId id) : Entity<SampleId>(id);

public sealed class GenericChild<T>(SampleId id) : Entity<SampleId>(id);

internal sealed class HiddenChild(SampleId id) : Entity<SampleId>(id);

public sealed class HiddenChildren : IEnumerable<HiddenChild>
{
    IEnumerator<HiddenChild> IEnumerable<HiddenChild>.GetEnumerator() =>
        Enumerable.Empty<HiddenChild>().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<HiddenChild>)this).GetEnumerator();
}

public sealed class ChildWithParentId(SampleId id) : Entity<SampleId>(id)
{
    public Guid ParentId { get; private set; }
}

internal sealed class InternalRoot;

public readonly struct StructRoot;

public abstract class AbstractRoot : AggregateRoot<SampleId>
{
    protected AbstractRoot(SampleId id) : base(id)
    {
    }
}

public sealed class GenericRoot<T>;

public sealed class UnrelatedRoot;

public sealed class WrongBaseRoot(SampleId id) : Entity<SampleId>(id);

public readonly record struct WrongValueId : IStronglyTypedId
{
    Guid IStronglyTypedId.Value => Guid.Empty;

    public int Value => 0;
}

public readonly record struct MissingValueId : IStronglyTypedId
{
    Guid IStronglyTypedId.Value => Guid.Empty;
}

public readonly record struct MissingFactoryId(Guid Value) : IStronglyTypedId;

public sealed class WrongValueRoot(WrongValueId id) : AggregateRoot<WrongValueId>(id);

public sealed class MissingValueRoot(MissingValueId id) : AggregateRoot<MissingValueId>(id);

public sealed class MissingFactoryRoot(MissingFactoryId id) : AggregateRoot<MissingFactoryId>(id);

public sealed class ForeignIdRoot(Foreign.Id id) : AggregateRoot<Foreign.Id>(id);

public sealed class MissingUpdateRoot(SampleId id) : AggregateRoot<SampleId>(id)
{
    public static Result<MissingUpdateRoot> Create() => Result.Success(new MissingUpdateRoot(default));

    public int Update() => 1;
}
