namespace Organization.Product.Module.Domain.Products.ValueObjects;

public sealed partial class ProductName : ValueObject
{
    public const int MaxLength = 120;

    private ProductName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<ProductName> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<ProductName>(
                Error.Validation("Product name cannot be empty.").WithField(nameof(ProductName)));
        }

        var name = value.Trim();
        if (name.Length > MaxLength)
        {
            return Result.Failure<ProductName>(
                Error.Validation($"Product name cannot exceed {MaxLength} characters.").WithField(nameof(ProductName)));
        }

        return Result.Success(new ProductName(name));
    }
}
