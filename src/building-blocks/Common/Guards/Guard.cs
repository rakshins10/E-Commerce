using System.Runtime.CompilerServices;
using ECommerce.Common.Exceptions;

namespace ECommerce.Common.Guards;

/// <summary>
/// Guard clauses for enforcing preconditions at the top of a constructor or method.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pattern:</b> Guard Clause. See <c>docs/concept-map.md</c>.
/// </para>
/// <para>
/// The value is not brevity — it is that a constructor which guards its arguments can never produce an invalid
/// object. Combined with immutability, that means <i>if you are holding one, it is valid</i>, and no downstream
/// code needs to re-check. This is the "parse, don't validate" idea applied to domain types, and it is what lets
/// the rest of the codebase stop defensively null-checking.
/// </para>
/// <para>
/// Every method uses <see cref="CallerArgumentExpressionAttribute"/>, so the failure message names the actual
/// expression at the call site — <c>Guard.AgainstNegative(item.Quantity)</c> reports <c>item.Quantity</c> rather
/// than a hand-typed string that will be wrong after the next rename.
/// </para>
/// <para>
/// These throw <see cref="DomainException"/> rather than <see cref="ArgumentException"/> when used inside
/// aggregates, because reaching one means an invariant was violated — see that type's remarks for why that is a
/// bug rather than an expected outcome.
/// </para>
/// </remarks>
public static class Guard
{
    /// <summary>Throws if <paramref name="value"/> is <see langword="null"/>.</summary>
    public static T AgainstNull<T>(T? value, [CallerArgumentExpression(nameof(value))] string? name = null)
        where T : class =>
        value ?? throw new DomainException($"{name} must not be null.");

    /// <summary>Throws if the string is null, empty, or entirely whitespace.</summary>
    public static string AgainstNullOrWhiteSpace(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? name = null) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainException($"{name} must not be null or whitespace.")
            : value;

    /// <summary>Throws if the value is zero or negative. Use for quantities and counts.</summary>
    public static int AgainstNonPositive(int value, [CallerArgumentExpression(nameof(value))] string? name = null) =>
        value <= 0 ? throw new DomainException($"{name} must be greater than zero, but was {value}.") : value;

    /// <summary>Throws if the value is negative. Zero is allowed — use for amounts and balances.</summary>
    public static decimal AgainstNegative(
        decimal value,
        [CallerArgumentExpression(nameof(value))] string? name = null) =>
        value < 0 ? throw new DomainException($"{name} must not be negative, but was {value}.") : value;

    /// <summary>Throws if the value is an empty <see cref="Guid"/>.</summary>
    public static Guid AgainstEmpty(Guid value, [CallerArgumentExpression(nameof(value))] string? name = null) =>
        value == Guid.Empty ? throw new DomainException($"{name} must not be an empty GUID.") : value;

    /// <summary>Throws if the string exceeds <paramref name="maxLength"/>.</summary>
    /// <remarks>
    /// Guards the length in the domain rather than relying on the database column to reject it. A truncation or
    /// constraint error surfacing from the persistence layer names a column, not a business rule, and arrives
    /// far from the code that caused it.
    /// </remarks>
    public static string AgainstTooLong(
        string value,
        int maxLength,
        [CallerArgumentExpression(nameof(value))] string? name = null) =>
        value.Length > maxLength
            ? throw new DomainException($"{name} must be at most {maxLength} characters, but was {value.Length}.")
            : value;

    /// <summary>Throws if the sequence is null or contains no elements.</summary>
    public static IReadOnlyCollection<T> AgainstEmptyCollection<T>(
        IReadOnlyCollection<T>? value,
        [CallerArgumentExpression(nameof(value))] string? name = null) =>
        value is null || value.Count == 0
            ? throw new DomainException($"{name} must contain at least one element.")
            : value;

    /// <summary>Throws with <paramref name="message"/> when <paramref name="condition"/> is true.</summary>
    /// <remarks>
    /// The escape hatch for rules with no dedicated guard — for example
    /// <c>Guard.Against(status != OrderStatus.Draft, "Only a draft order may be submitted.")</c>. Prefer a named
    /// guard where one fits; a named guard reads as a rule, whereas this reads as a condition.
    /// </remarks>
    public static void Against(bool condition, string message)
    {
        if (condition)
        {
            throw new DomainException(message);
        }
    }
}
