using System.Data;

using Dapper;

using ECommerce.BackOffice.Api.Infrastructure;

namespace ECommerce.BackOffice.Api.Features;

/// <summary>
/// A record of what staff did.
/// </summary>
/// <remarks>
/// <para>
/// <b>Append-only, and that is the entire security property.</b> There is no update and no delete — not
/// as an oversight, but because an audit log somebody can edit is not evidence of anything. If the code
/// cannot modify a row, a compromised service cannot cover its tracks.
/// </para>
/// <para>
/// <b>Why a separate log when every service already logs.</b> Application logs answer "what did the
/// system do"; an audit log answers "who did this, and were they allowed to". They have different
/// audiences, different retention, and different consequences for loss. Logs are sampled and rotated;
/// this is not.
/// </para>
/// <para>
/// The distinction that matters most: an audit entry records a <b>human decision</b>. An order moving
/// from Paid to Shipped because the saga said so is not audited; a manager cancelling somebody's order is.
/// </para>
/// </remarks>
public sealed class AuditEntry
{
    private AuditEntry()
    {
        // EF Core.
    }

    public AuditEntry(string actorId, string actorName, string action, string target, string? detail)
    {
        Id = Guid.CreateVersion7();
        ActorId = actorId;
        ActorName = actorName;
        Action = action;
        Target = target;
        Detail = detail;
        OccurredAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    /// <summary>Keycloak <c>sub</c>. Immutable, unlike a username.</summary>
    public string ActorId { get; private set; } = string.Empty;

    /// <summary>
    /// The username as it was at the time.
    /// </summary>
    /// <remarks>
    /// A copy, for the same reason an order copies its address: the log must still read correctly when
    /// somebody changes their username or leaves. Resolving the name at read time would rewrite history.
    /// </remarks>
    public string ActorName { get; private set; } = string.Empty;

    /// <summary>What they did — <c>order.cancelled</c>, <c>stock.adjusted</c>, <c>user.disabled</c>.</summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>What they did it to. An order number, a SKU, a username.</summary>
    public string Target { get; private set; } = string.Empty;

    public string? Detail { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>Writes audit entries.</summary>
/// <remarks>
/// A single method, and no way to read or modify. Narrow on purpose: a service that can only append
/// cannot be used to tamper.
/// </remarks>
public sealed class AuditWriter(BackOfficeDbContext db)
{
    public async Task RecordAsync(
        string actorId,
        string actorName,
        string action,
        string target,
        string? detail = null,
        CancellationToken cancellationToken = default)
    {
        db.AuditEntries.Add(new AuditEntry(actorId, actorName, action, target, detail));
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>An audit entry as the admin panel reads it.</summary>
/// <remarks>
/// <b>Init properties, not a positional record</b>, and the distinction is load-bearing. PostgreSQL
/// folds an unquoted alias to lowercase, so <c>AS ActorName</c> arrives as <c>actorname</c>. Dapper
/// matches PROPERTIES case-insensitively and finds it; it matches CONSTRUCTOR PARAMETERS
/// case-sensitively and does not - failing at runtime with "no parameterless default constructor or
/// one matching signature", which names the constructor rather than the real cause.
///
/// Every other read DTO in this repo already uses init properties, which is why this is the only place
/// it bit.
/// </remarks>
public sealed record AuditEntryDto
{
    public string ActorName { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public string? Detail { get; init; }

    public DateTimeOffset OccurredAt { get; init; }
}

/// <summary>Reads the audit log.</summary>
public sealed class AuditQueries([FromKeyedServices("backoffice")] IDbConnection connection)
{
    public async Task<IReadOnlyList<AuditEntryDto>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        // Clamped. An unbounded limit on an append-only table that only grows is a denial of service
        // with a query string.
        int safeLimit = Math.Clamp(limit, 1, 200);

        const string sql = """
            SELECT actor_name  AS ActorName,
                   action      AS Action,
                   target      AS Target,
                   detail      AS Detail,
                   occurred_at AS OccurredAt
            FROM   audit_entries
            ORDER  BY occurred_at DESC, id DESC
            LIMIT  @Limit;
            """;

        return (await connection.QueryAsync<AuditEntryDto>(
            new CommandDefinition(sql, new { Limit = safeLimit }, cancellationToken: cancellationToken)))
            .ToArray();
    }
}
