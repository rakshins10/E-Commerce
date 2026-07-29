using ECommerce.Common.Exceptions;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ECommerce.Observability;

/// <summary>
/// Turns a <see cref="DomainException"/> into a 400 with an RFC 7807 problem detail.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it lives in the Observability block rather than in Common.</b> It needs ASP.NET Core, and
/// Common is referenced by every DOMAIN project - giving Common a framework dependency would make the
/// domain transitively depend on ASP.NET and break the layering rule the architecture tests assert.
/// Observability already owns the correlation middleware and is referenced by every service, and this
/// handler's job is largely to log correctly and attach the correlation id, so the fit is closer than
/// the name suggests.
/// </para>
/// <para>
/// <b>Why this exists.</b> A broken business rule is not a server fault — it is the server correctly
/// refusing. Without translation, ASP.NET Core returns a 500 with the exception type and stack trace in
/// the body, which is wrong twice over: the status code lies about whose fault it is, and the body leaks
/// the internal structure of the application to whoever asked.
/// </para>
/// <para>
/// The message itself is deliberately passed through, because domain messages are written for the person
/// who triggered them: <i>"SKU 'AUR-HP-001' is already in use"</i>, <i>"An order may not contain more
/// than 50 distinct items"</i>. That is only safe because <see cref="DomainException"/> is thrown by the
/// domain, never wrapped around an infrastructure failure — a rule this handler quietly depends on.
/// </para>
/// <para>
/// <b>Everything else stays a 500.</b> A <c>NullReferenceException</c> is a bug and should look like one:
/// logged as an error, reported without detail, and not dressed up as a client mistake. Catching broadly
/// here would hide real faults behind polite 400s, which is how a service comes to look healthy while
/// failing every request.
/// </para>
/// </remarks>
public sealed class DomainExceptionHandler(ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (exception is not DomainException domainException)
        {
            // Not ours. Returning false lets the next handler - or the default 500 - deal with it.
            return false;
        }

        // Information, not Error. A refused request is the system working; logging it as an error
        // trains people to ignore the error log, which is where the real faults are.
        logger.LogInformation(
            "Rejected {Method} {Path}: {Reason}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            domainException.Message);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "The request was refused",
            Detail = domainException.Message,
            Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.1",
            Instance = httpContext.Request.Path,
        };

        // The correlation id goes in the body, so a customer quoting an error message gives support
        // something to search Seq for.
        if (httpContext.Items.TryGetValue(
                CorrelationId.ItemsKey, out object? correlationId))
        {
            problem.Extensions["correlationId"] = correlationId;
        }

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }
}

/// <summary>Registration helpers.</summary>
public static class DomainExceptionHandlerExtensions
{
    /// <summary>
    /// Registers the handler and ASP.NET Core's problem-details service.
    /// </summary>
    /// <remarks>
    /// <c>AddProblemDetails</c> is what makes unhandled exceptions and bare status codes render as
    /// problem documents too, so a 404 from <c>Results.NotFound()</c> and a 400 from a broken invariant
    /// have the same shape. A client that has to parse two error formats will eventually only handle
    /// one.
    /// </remarks>
    public static IServiceCollection AddDomainExceptionHandling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails();
        services.AddExceptionHandler<DomainExceptionHandler>();

        return services;
    }
}
