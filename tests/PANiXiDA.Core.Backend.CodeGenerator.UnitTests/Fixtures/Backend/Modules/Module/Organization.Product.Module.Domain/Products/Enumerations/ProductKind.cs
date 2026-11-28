namespace Organization.Product.Module.Domain.Products.Enumerations;

public sealed partial class ProductKind : Enumeration<ProductKind>
{
    public static readonly ProductKind Physical = new(1, nameof(Physical));
    public static readonly ProductKind Digital = new(2, nameof(Digital));

    private ProductKind(int id, string name)
        : base(id, name)
    {
    }

    public static Result<ProductKind> Create(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && TryFromName(value.Trim(), out var kind) && kind is not null)
        {
            return Result.Success(kind);
        }

        return Result.Failure<ProductKind>(
            Error.Validation("Product kind must be Physical or Digital.").WithField(nameof(ProductKind)));
    }
}
