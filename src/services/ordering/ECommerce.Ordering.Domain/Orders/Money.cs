using ECommerce.Common.Exceptions;
using ECommerce.Common.Guards;
using ECommerce.Common.SeedWork;

namespace ECommerce.Ordering.Domain.Orders;

/// <summary>
/// An amount together with the currency it is denominated in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this type exists at all.</b> A bare <c>decimal</c> for a price is the most common modelling
/// mistake in a shopping system, and it fails silently: nothing stops you adding a price in pounds to a
/// price in euros, and the result looks entirely plausible. Making currency part of the type turns that
/// into an exception at the point of the mistake instead of a wrong number on an invoice.
/// </para>
/// <para>
/// <b>Why <c>decimal</c> and not <c>double</c>.</b> Binary floating point cannot represent 0.1 exactly.
/// Summing a basket of ten items priced 0.10 gives 0.9999999999999999, and a total that is a penny out
/// once in a thousand orders is a support ticket nobody can reproduce. <c>decimal</c> is base-10 and
/// exact for the values money actually takes.
/// </para>
/// <para>
/// This is a <see cref="ValueObject"/>: two amounts of £10 are interchangeable, so equality is
/// structural and there is no identity to track.
/// </para>
/// </remarks>
public sealed class Money : ValueObject
{
    private Money()
    {
        // EF Core.
    }

    public Money(decimal amount, string currency)
    {
        Guard.AgainstNegative(amount);
        Guard.AgainstNullOrWhiteSpace(currency);
        Guard.Against(currency.Length != 3, "Currency must be an ISO 4217 three-letter code.");

        // Rounded on construction, so it is impossible to hold an amount that cannot be charged.
        // MidpointRounding.ToEven ("banker's rounding") is the accountancy default: always rounding
        // .5 up introduces a systematic upward bias across many transactions.
        Amount = Math.Round(amount, 2, MidpointRounding.ToEven);
        Currency = currency.ToUpperInvariant();
    }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = "GBP";

    public static Money Zero(string currency) => new(0m, currency);

    public Money Add(Money other)
    {
        AssertSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        AssertSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    /// <summary>Multiplies by a whole quantity — a line total.</summary>
    public Money Multiply(int quantity)
    {
        Guard.AgainstNonPositive(quantity);
        return new Money(Amount * quantity, Currency);
    }

    public bool IsGreaterThan(Money other)
    {
        AssertSameCurrency(other);
        return Amount > other.Amount;
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    private void AssertSameCurrency(Money other)
    {
        Guard.AgainstNull(other);

        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            // Deliberately an exception rather than a silent conversion. Converting would need an
            // exchange rate, and the rate that matters is the one at the time of the transaction —
            // a decision this type has no business making on its own.
            throw new DomainException(
                $"Cannot combine {Currency} with {other.Currency}. Convert explicitly first.");
        }
    }
}
