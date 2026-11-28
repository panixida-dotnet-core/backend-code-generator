namespace Organization.Product.Module.Domain.Products;

public readonly record struct ProductId : IStronglyTypedId
{
    private ProductId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static ProductId New() => new(Guid.CreateVersion7());

    public static Result<ProductId> Create(Guid value) =>
        value == Guid.Empty
            ? Result.Failure<ProductId>(Error.Validation("Product id cannot be empty."))
            : Result.Success(new ProductId(value));

    public override string ToString() => Value.ToString();
}
