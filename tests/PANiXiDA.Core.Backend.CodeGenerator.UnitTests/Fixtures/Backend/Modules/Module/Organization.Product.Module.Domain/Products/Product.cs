using Organization.Product.Module.Domain.Products.Entities.ProductVariants;
using Organization.Product.Module.Domain.Products.Enumerations;
using Organization.Product.Module.Domain.Products.ValueObjects;

namespace Organization.Product.Module.Domain.Products;

public sealed class Product : AggregateRoot<ProductId>
{
    private readonly List<ProductVariant> _variants = [];

    private Product(ProductId id, ProductName name, ProductKind kind, ProductDescription? description, Money? listPrice)
        : base(id)
    {
        Name = name;
        Kind = kind;
        Description = description;
        ListPrice = listPrice;
    }

    public ProductName Name { get; private set; }
    public ProductKind Kind { get; private set; }
    public ProductDescription? Description { get; private set; }
    public Money? ListPrice { get; private set; }

    public IReadOnlyCollection<ProductVariant> Variants => _variants.AsReadOnly();

    public static Result<Product> Create(
        ProductName name,
        ProductKind kind,
        ProductDescription? description,
        Money? listPrice) =>
        Result.Success(new Product(
            id: ProductId.New(),
            name: name,
            kind: kind,
            description: description,
            listPrice: listPrice));

    public Result Update(ProductName name, ProductKind kind, ProductDescription? description, Money? listPrice)
    {
        Name = name;
        Kind = kind;
        Description = description;
        ListPrice = listPrice;
        return Result.Success();
    }

    public Result<ProductVariantId> AddVariant(ProductSku sku, Money price, InventoryStock stock)
    {
        var variantResult = ProductVariant.Create(sku: sku, price: price, stock: stock);
        if (variantResult.IsFailure)
        {
            return Result.Failure<ProductVariantId>(variantResult.Errors);
        }

        var variant = variantResult.Value;
        _variants.Add(variant);
        return Result.Success(variant.Id);
    }

    public Result UpdateVariant(ProductVariantId id, ProductSku sku, Money price, InventoryStock stock)
    {
        var variant = _variants.Find(item => item.Id == id);
        return variant is null
            ? Result.Failure(Error.NotFound("Product variant was not found."))
            : variant.Update(sku: sku, price: price, stock: stock);
    }

    public Result RemoveVariant(ProductVariantId id)
    {
        return _variants.RemoveAll(item => item.Id == id) > 0
            ? Result.Success()
            : Result.Failure(Error.NotFound("Product variant was not found."));
    }
}
