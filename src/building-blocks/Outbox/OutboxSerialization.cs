using System.Text.Json;

namespace ECommerce.Outbox;

/// <summary>
/// The one set of JSON options used to write the outbox and to read it back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shared deliberately, because splitting them is a silent bug.</b> The writer originally used
/// <see cref="JsonSerializerDefaults.Web"/> (camelCase, case-insensitive) and the publisher used the
/// library defaults (PascalCase, case-sensitive). Every event serialised perfectly, committed
/// atomically with its order, and then failed to deserialise on the way out — so orders were created
/// correctly and no other service ever heard about them.
/// </para>
/// <para>
/// The failure mode is worth dwelling on: the writer is on the request path and the publisher is a
/// background loop, so nothing the customer did returned an error. It was visible only as rows in
/// <c>outbox_messages</c> with a rising <c>attempts</c> count — which is exactly why that column and
/// <c>last_error</c> exist, and why the publisher records failures instead of just retrying.
/// </para>
/// <para>
/// Web defaults are the right choice for both: the payload is stored as <c>jsonb</c> and read by humans
/// during diagnosis, and matching how the rest of the system serialises means an event looks the same in
/// the database as it does on the wire.
/// </para>
/// </remarks>
public static class OutboxSerialization
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
