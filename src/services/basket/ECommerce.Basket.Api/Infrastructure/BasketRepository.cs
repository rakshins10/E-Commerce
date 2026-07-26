using System.Text.Json;

using ECommerce.Basket.Api.Model;

using StackExchange.Redis;

namespace ECommerce.Basket.Api.Infrastructure;

/// <summary>
/// Basket storage, in Redis.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why Redis rather than PostgreSQL, when every other service uses PostgreSQL.</b> This is the
/// clearest example of <b>polyglot persistence</b> in the repo: each service picks the store that fits
/// its data, precisely because each service owns its own.
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     <b>The access pattern is a key/value lookup.</b> Every read is "give me this customer's basket"
///     and every write replaces it wholesale. There is not one query that joins, filters or aggregates,
///     so nothing a relational database is good at is being used.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>The data is disposable.</b> Losing a basket is an annoyance; losing an order is an incident.
///     Accepting weaker durability guarantees for basket data is a legitimate trade, and it is only
///     available because baskets are not in the orders database.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Expiry is built in.</b> Abandoned baskets should disappear. Redis does that with a TTL;
///     PostgreSQL needs a scheduled job that somebody has to write, run and monitor.
///     </description>
///   </item>
/// </list>
/// <para>
/// The counterweight, stated honestly: this is another technology to operate, back up and understand.
/// For a smaller system, a <c>baskets</c> table would be a perfectly reasonable answer.
/// </para>
/// </remarks>
public sealed class BasketRepository(IConnectionMultiplexer redis, ILogger<BasketRepository> logger)
{
    /// <summary>
    /// How long an untouched basket survives.
    /// </summary>
    /// <remarks>
    /// Thirty days is a product decision, not a technical one: long enough that a customer who returns
    /// next week finds their basket, short enough that Redis is not storing years of abandoned
    /// intentions. The TTL is refreshed on every write, so an active basket never expires underneath
    /// someone.
    /// </remarks>
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Namespaced key. <c>basket:{sub}</c>.
    /// </summary>
    /// <remarks>
    /// Redis is a single flat keyspace shared by everything that connects to it. A prefix is what stops
    /// this service's keys colliding with a cache entry someone adds next year, and it makes
    /// <c>SCAN basket:*</c> possible when diagnosing.
    /// </remarks>
    private static string KeyFor(string buyerId) => $"basket:{buyerId}";

    public async Task<CustomerBasket?> GetAsync(string buyerId)
    {
        IDatabase db = redis.GetDatabase();
        RedisValue data = await db.StringGetAsync(KeyFor(buyerId)).ConfigureAwait(false);

        if (data.IsNullOrEmpty)
        {
            return null;
        }

        try
        {
            // Cast explicitly: RedisValue converts implicitly to both string and ReadOnlyMemory<byte>,
            // so the overload is ambiguous without it.
            return JsonSerializer.Deserialize<CustomerBasket>((string)data!, SerializerOptions);
        }
        catch (JsonException ex)
        {
            // A basket written by an older version of this service whose shape no longer deserialises.
            // Treated as absent rather than as an error: a customer with an empty basket can carry on
            // shopping, whereas a 500 on every page load cannot be worked around. Deliberately logged at
            // warning, because if this happens often the schema change was not backward compatible.
            logger.LogWarning(ex, "Discarding unreadable basket for {BuyerId}.", buyerId);
            return null;
        }
    }

    public async Task<CustomerBasket> SaveAsync(CustomerBasket basket)
    {
        IDatabase db = redis.GetDatabase();

        await db.StringSetAsync(
            KeyFor(basket.BuyerId),
            JsonSerializer.Serialize(basket, SerializerOptions),
            Ttl)
            .ConfigureAwait(false);

        return basket;
    }

    /// <summary>Removes the basket entirely.</summary>
    /// <remarks>
    /// Called when an order is placed. Leaving the basket behind means a customer who refreshes after
    /// checkout sees the items they have just bought still sitting there, and buys them again.
    /// </remarks>
    public async Task DeleteAsync(string buyerId) =>
        await redis.GetDatabase().KeyDeleteAsync(KeyFor(buyerId)).ConfigureAwait(false);
}
