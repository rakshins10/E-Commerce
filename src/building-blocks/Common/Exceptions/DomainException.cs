namespace ECommerce.Common.Exceptions;

/// <summary>
/// Thrown when a domain <b>invariant</b> is violated — a rule that must hold for an aggregate to be valid at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pattern:</b> Invariant enforcement inside the aggregate (DDD tactical).
/// See <c>docs/domain/business-rules.md</c>.
/// </para>
/// <para>
/// <b>When to throw this rather than return a failed <c>Result</c>.</b> The two are not interchangeable:
/// </para>
/// <list type="bullet">
///   <item><description>A <c>Result</c> failure is an <i>expected</i> outcome the caller must handle —
///   "that order does not exist", "you cannot cancel a shipped order". The application layer checks first and
///   returns a failure; nothing is broken.</description></item>
///   <item><description>A <see cref="DomainException"/> means the aggregate was asked to enter a state that
///   <i>cannot exist</i> — an order line with negative quantity, a total that disagrees with its lines. Reaching
///   this is a bug: the application layer should have rejected the request before the aggregate ever saw it.</description></item>
/// </list>
/// <para>
/// So this exception is a <b>last line of defence</b>, not the primary validation mechanism. It exists because
/// an aggregate must be able to guarantee its own consistency even when called incorrectly — that guarantee is
/// the entire reason aggregates exist. In a healthy system these are never thrown in production, and when one is,
/// it should be treated as a defect rather than handled and swallowed.
/// </para>
/// <para>
/// The global exception handler maps this to <b>422 Unprocessable Entity</b> and logs it at error level with the
/// correlation id, so it is investigable rather than merely reported.
/// </para>
/// </remarks>
public class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public DomainException()
    {
    }
}
