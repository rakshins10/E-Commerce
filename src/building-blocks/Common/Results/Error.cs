namespace ECommerce.Common.Results;

/// <summary>
/// Classifies a failure so that a single mapping can turn it into the right HTTP status code without every
/// handler knowing about HTTP.
/// </summary>
public enum ErrorType
{
    /// <summary>The request was malformed or violated a validation rule. → 400.</summary>
    Validation,

    /// <summary>The requested resource does not exist. → 404.</summary>
    NotFound,

    /// <summary>The request is well-formed but conflicts with current state, e.g. a version conflict. → 409.</summary>
    Conflict,

    /// <summary>A domain invariant or business rule forbids the operation. → 422.</summary>
    /// <remarks>
    /// Distinct from <see cref="Validation"/> on purpose. "Quantity must be positive" is validation and can be
    /// checked without loading anything. "You cannot cancel a shipped order" is a <i>business rule</i> that
    /// depends on state. Collapsing the two into 400 loses information the client can act on.
    /// </remarks>
    BusinessRule,

    /// <summary>The caller is authenticated but not permitted. → 403.</summary>
    Forbidden,

    /// <summary>A dependency failed in a way the caller cannot fix. → 502/503.</summary>
    Unavailable,
}

/// <summary>
/// A failure: a stable machine-readable code, a human-readable description, and a type that determines how it
/// surfaces over HTTP.
/// </summary>
/// <param name="Code">A stable dotted identifier, e.g. <c>order.cannot_cancel_shipped</c>. Clients may branch on
/// this; they must never branch on <paramref name="Description"/>, which is prose and may be reworded or
/// localised.</param>
/// <param name="Description">Human-readable explanation, safe to show a developer. Never include secrets or
/// internal detail — this can reach a client.</param>
/// <param name="Type">Determines the HTTP status code.</param>
public sealed record Error(string Code, string Description, ErrorType Type = ErrorType.Validation)
{
    /// <summary>The absence of an error. Used by <see cref="Result"/> internals; never returned to a caller.</summary>
    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);

    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);

    public static Error BusinessRule(string code, string description) =>
        new(code, description, ErrorType.BusinessRule);

    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);

    public static Error Unavailable(string code, string description) =>
        new(code, description, ErrorType.Unavailable);

    public override string ToString() => $"{Code}: {Description}";
}
