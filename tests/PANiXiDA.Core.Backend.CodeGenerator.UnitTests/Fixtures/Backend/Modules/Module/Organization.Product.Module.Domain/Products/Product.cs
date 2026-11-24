using Organization.Product.Module.Domain.Products.Enumerations;
using Organization.Product.Module.Domain.Products.ValueObjects;

namespace Organization.Product.Module.Domain.Products;

public sealed class Product : AggregateRoot<ProductId>
{
    private readonly List<ProductVariant> _variants = [];

    private Product(ProductId id, ProductName name, ProductKind kind, string? description, Money? listPrice)
        : base(id)
    {
        Name = name;
        Kind = kind;
        Description = description;
        ListPrice = listPrice;
    }

    public ProductName Name { get; private set; }
    public ProductKind Kind { get; private set; }
    public string? Description { get; private set; }
    public Money? ListPrice { get; private set; }

    public IReadOnlyCollection<ProductVariant> Variants => _variants;

    public static Result<Product> Create(string name, string kind, string? description, Money? listPrice)
    {
        var nameResult = ProductName.Create(name);
        var kindResult = ProductKind.Create(kind);
        var validation = Result.Combine(nameResult, kindResult);
        if (validation.IsFailure)
        {
            return Result.Failure<Product>(validation.Errors);
        }

        return Result.Success(new Product(ProductId.New(), nameResult.Value, kindResult.Value, description, listPrice));
    }

    public Result Update(string name, string kind, string? description, Money? listPrice)
    {
        var nameResult = ProductName.Create(name);
        var kindResult = ProductKind.Create(kind);
        var validation = Result.Combine(nameResult, kindResult);
        if (validation.IsFailure)
        {
            return Result.Failure(validation.Errors);
        }

        Name = nameResult.Value;
        Kind = kindResult.Value;
        Description = description;
        ListPrice = listPrice;
        return Result.Success();
    }

    public Result<ProductVariantId> AddVariant(string sku, Money price, int stock)
    {
        var variantResult = ProductVariant.Create(sku, price, stock);
        if (variantResult.IsFailure)
        {
            return Result.Failure<ProductVariantId>(variantResult.Errors);
        }

        var variant = variantResult.Value;
        _variants.Add(variant);
        return Result.Success(variant.Id);
    }

    public Result UpdateVariant(ProductVariantId id, string sku, Money price, int stock)
    {
        var variant = _variants.Find(item => item.Id == id);
        return variant is null
            ? Result.Failure(Error.NotFound("Product variant was not found."))
            : variant.Update(sku, price, stock);
    }

    public Result RemoveVariant(ProductVariantId id)
    {
        return _variants.RemoveAll(item => item.Id == id) > 0
            ? Result.Success()
            : Result.Failure(Error.NotFound("Product variant was not found."));
    }
}
