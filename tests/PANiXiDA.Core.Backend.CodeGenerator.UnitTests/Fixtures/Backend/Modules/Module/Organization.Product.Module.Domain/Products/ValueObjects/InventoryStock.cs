namespace Organization.Product.Module.Domain.Products.ValueObjects;

public sealed partial class InventoryStock : ValueObject
{
    private InventoryStock(int value)
    {
        Value = value;
    }

    public int Value { get; }

    public static Result<InventoryStock> Create(int value) =>
        value < 0
            ? Result.Failure<InventoryStock>(
                Error.Validation("Stock cannot be negative.").WithField(nameof(InventoryStock)))
            : Result.Success(new InventoryStock(value));
}
