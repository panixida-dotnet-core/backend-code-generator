namespace Organization.Product.Module.Domain.Products.ValueObjects;

public sealed partial class Money : ValueObject
{
    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }
    public string Currency { get; }

    public static Result<Money> Create(decimal amount, string currency)
    {
        var currencyCode = currency?.Trim().ToUpperInvariant();
        var amountResult = amount < 0
            ? Result.Failure(Error.Validation("Amount cannot be negative.").WithField(nameof(Amount)))
            : Result.Success();
        var currencyResult = currencyCode is not ("RUB" or "USD" or "EUR")
            ? Result.Failure(Error.Validation("Currency must be RUB, USD or EUR.").WithField(nameof(Currency)))
            : Result.Success();
        var validation = Result.Combine(amountResult, currencyResult);

        return validation.IsFailure
            ? Result.Failure<Money>(validation.Errors)
            : Result.Success(new Money(amount, currencyCode!));
    }

    public static Result<Money?> Create(decimal? amount, string? currency)
    {
        if (amount is null && currency is null)
        {
            return Result.Success<Money?>(null);
        }

        if (amount is null)
        {
            return Result.Failure<Money?>(Error.Validation("Amount is required.").WithField(nameof(Amount)));
        }

        return Create(amount.Value, currency ?? string.Empty).Map(static money => (Money?)money);
    }
}
