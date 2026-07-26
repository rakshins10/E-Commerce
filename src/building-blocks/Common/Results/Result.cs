namespace ECommerce.Common.Results;

/// <summary>
/// The outcome of an operation that can fail for an <i>expected</i> reason: success, or an <see cref="Error"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pattern:</b> Result / Railway-oriented programming. See <c>docs/concept-map.md</c>.
/// </para>
/// <para>
/// <b>Why not just throw?</b> Exceptions are for the <i>exceptional</i> — a database that is unreachable, a bug.
/// "This order cannot be cancelled because it has shipped" is not exceptional; it is an ordinary, anticipated
/// outcome that the caller must handle. Modelling it as a return value has three concrete benefits:
/// </para>
/// <list type="bullet">
///   <item><description><b>It is visible in the signature.</b> <c>Task&lt;Result&gt;</c> tells a caller failure is
///   possible. <c>Task</c> that might throw tells them nothing, and the compiler will not remind them.</description></item>
///   <item><description><b>It is far cheaper.</b> Throwing captures a stack trace and unwinds; on a path that
///   fails routinely (validation on user input) that cost is real and needless.</description></item>
///   <item><description><b>It does not lie about control flow.</b> Exceptions used for expected outcomes make
///   ordinary code paths invisible in the source.</description></item>
/// </list>
/// <para>
/// <b>The rule applied in this codebase:</b> expected failures return <see cref="Result"/>; genuine invariant
/// violations inside an aggregate throw <c>DomainException</c>. An aggregate whose invariant is broken is a bug
/// — it should never have been reachable, because the application layer should have returned a failed
/// <see cref="Result"/> first. The exception is the last line of defence, not the mechanism.
/// </para>
/// </remarks>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        // A successful result carrying an error, or a failure carrying none, is always a programming mistake -
        // and one that would otherwise surface far from its cause.
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

/// <summary>
/// A <see cref="Result"/> that carries a value on success.
/// </summary>
/// <typeparam name="TValue">The success value type.</typeparam>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) => _value = value;

    /// <summary>
    /// The success value.
    /// </summary>
    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    /// <remarks>
    /// Throws rather than returning <see langword="null"/> so that forgetting to check <see cref="Result.IsSuccess"/>
    /// fails loudly and immediately, instead of producing a null that surfaces somewhere unrelated.
    /// </remarks>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access the value of a failed result.");

    /// <summary>
    /// Implicit lift from a value, so handlers can <c>return order.Id;</c> rather than
    /// <c>return Result.Success(order.Id);</c>.
    /// </summary>
    public static implicit operator Result<TValue>(TValue value) => Success(value);

    /// <summary>
    /// Collapses both branches into a single value — the exhaustive way to consume a result, since the compiler
    /// requires both cases to be supplied.
    /// </summary>
    public TResult Match<TResult>(Func<TValue, TResult> onSuccess, Func<Error, TResult> onFailure) =>
        IsSuccess ? onSuccess(Value) : onFailure(Error);
}
