namespace Organization.Product.Module.Domain.Warehouses;

public sealed class Warehouse : AggregateRoot<WarehouseId>
{
    private Warehouse(WarehouseId id, string name)
        : base(id)
    {
        Name = name;
    }

    public string Name { get; private set; }

    public static Result<Warehouse> Create(string name) => Result.Success(new Warehouse(WarehouseId.New(), name));

    public Result Update(string name)
    {
        Name = name;
        return Result.Success();
    }
}
