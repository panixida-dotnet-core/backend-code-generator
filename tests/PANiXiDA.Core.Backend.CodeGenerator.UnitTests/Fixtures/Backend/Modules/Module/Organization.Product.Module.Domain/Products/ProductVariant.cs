using Organization.Product.Module.Domain.Products.ValueObjects;

namespace Organization.Product.Module.Domain.Products;

public sealed class ProductVariant : Entity<ProductVariantId>
{
    private ProductVariant(ProductVariantId id, string sku, Money price, int stock)
        : base(id)
    {
        Sku = sku;
        Price = price;
        Stock = stock;
    }

    public string Sku { get; private set; }
    public Money Price { get; private set; }
    public int Stock { get; private set; }

    internal static Result<ProductVariant> Create(string sku, Money price, int stock)
    {
        var validation = Validate(sku, stock);
        return validation.IsFailure
            ? Result.Failure<ProductVariant>(validation.Errors)
            : Result.Success(new ProductVariant(ProductVariantId.New(), sku.Trim(), price, stock));
    }

    internal Result Update(string sku, Money price, int stock)
    {
        var validation = Validate(sku, stock);
        if (validation.IsFailure)
        {
            return validation;
        }

        Sku = sku.Trim();
        Price = price;
        Stock = stock;
        return Result.Success();
    }

    private static Result Validate(string sku, int stock)
    {
        var skuResult = string.IsNullOrWhiteSpace(sku)
            ? Result.Failure(Error.Validation("SKU cannot be empty.").WithField(nameof(Sku)))
            : Result.Success();
        var stockResult = stock < 0
            ? Result.Failure(Error.Validation("Stock cannot be negative.").WithField(nameof(Stock)))
            : Result.Success();

        return Result.Combine(skuResult, stockResult);
    }
}
