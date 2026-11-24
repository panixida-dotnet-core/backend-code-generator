namespace Organization.Product.Module.Domain.Warehouses;

public readonly record struct WarehouseId : IStronglyTypedId
{
    private WarehouseId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static WarehouseId New() => new(Guid.CreateVersion7());

    public static Result<WarehouseId> Create(Guid value) =>
        value == Guid.Empty
            ? Result.Failure<WarehouseId>(Error.Validation("Warehouse id cannot be empty."))
            : Result.Success(new WarehouseId(value));

    public override string ToString() => Value.ToString();
}
