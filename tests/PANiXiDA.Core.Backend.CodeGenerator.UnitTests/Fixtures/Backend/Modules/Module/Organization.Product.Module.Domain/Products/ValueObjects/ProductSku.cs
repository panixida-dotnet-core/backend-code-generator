namespace Organization.Product.Module.Domain.Products.ValueObjects;

public sealed partial class ProductSku : ValueObject
{
    private ProductSku(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<ProductSku> Create(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? Result.Failure<ProductSku>(Error.Validation("SKU cannot be empty.").WithField(nameof(ProductSku)))
            : Result.Success(new ProductSku(value.Trim()));
}
