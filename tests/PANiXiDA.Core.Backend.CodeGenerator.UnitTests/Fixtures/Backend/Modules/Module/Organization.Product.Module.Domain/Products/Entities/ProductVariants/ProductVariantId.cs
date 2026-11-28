namespace Organization.Product.Module.Domain.Products.Entities.ProductVariants;

public readonly record struct ProductVariantId : IStronglyTypedId
{
    private ProductVariantId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static ProductVariantId New() => new(Guid.CreateVersion7());

    public static Result<ProductVariantId> Create(Guid value) =>
        value == Guid.Empty
            ? Result.Failure<ProductVariantId>(Error.Validation("Product variant id cannot be empty."))
            : Result.Success(new ProductVariantId(value));

    public override string ToString() => Value.ToString();
}
