using Organization.Product.Module.Domain.Products.ValueObjects;

namespace Organization.Product.Module.Domain.Products.Entities.ProductVariants;

public sealed class ProductVariant : Entity<ProductVariantId>
{
    private ProductVariant(ProductVariantId id, ProductSku sku, Money price, InventoryStock stock)
        : base(id)
    {
        Sku = sku;
        Price = price;
        Stock = stock;
    }

    public ProductSku Sku { get; private set; }
    public Money Price { get; private set; }
    public InventoryStock Stock { get; private set; }

    internal static Result<ProductVariant> Create(ProductSku sku, Money price, InventoryStock stock) =>
        Result.Success(new ProductVariant(id: ProductVariantId.New(), sku: sku, price: price, stock: stock));

    internal Result Update(ProductSku sku, Money price, InventoryStock stock)
    {
        Sku = sku;
        Price = price;
        Stock = stock;
        return Result.Success();
    }
}
