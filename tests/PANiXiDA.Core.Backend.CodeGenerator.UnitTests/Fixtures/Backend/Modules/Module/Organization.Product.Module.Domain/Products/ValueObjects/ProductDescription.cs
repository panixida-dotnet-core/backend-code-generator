namespace Organization.Product.Module.Domain.Products.ValueObjects;

public sealed partial class ProductDescription : ValueObject
{
    private ProductDescription(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<ProductDescription?> Create(string? value) =>
        Result.Success(value is null ? null : new ProductDescription(value.Trim()));
}
