# Basket service

> **Bounded context:** Basket (supporting) · **Port:** 5002 · **Store:** Redis
> **Code:** [`src/services/basket/ECommerce.Basket.Api`](../../src/services/basket/ECommerce.Basket.Api/)
> **Related:** [Ordering](ordering.md) · [Bounded contexts](../domain/bounded-contexts.md)

## Purpose

What a customer is thinking about buying. Add, change quantity, remove, empty.

That is the whole service, and its most interesting property is how little it does.

---

## Why this is a plain class and not a DDD aggregate

Ordering gets an aggregate root, value objects, domain events and a state machine.
[`CustomerBasket`](../../src/services/basket/ECommerce.Basket.Api/Model/CustomerBasket.cs) is a class with a
list and two computed properties. That is the correct answer, not a corner cut.

A basket has **almost no rules**. It is a scratchpad the customer edits freely: add anything, remove
anything, abandon it entirely. Nothing about it must be true for the business to work. Compare an order,
where "the total equals the sum of the lines" must hold at every instant, cancellation is legal in some
states and not others, and getting it wrong means charging the wrong amount.

The two limits that do exist — 50 distinct products, 100 of each — are there to stop a script making the
basket endpoint slow for everybody, not because the business cares.

> **Applying Ordering's ceremony here would be solving a problem that is not present.** That is how DDD
> earns its reputation for weight: not because the patterns are wrong, but because they get applied
> uniformly instead of where the complexity actually is. Spending the complexity budget in the right place
> is the skill.

---

## Why Redis, when every other service uses PostgreSQL

The clearest example of **polyglot persistence** in this repo — and it is only possible because each
service owns its own data.

| Reason | Detail |
|--------|--------|
| **The access pattern is key/value** | Every read is "give me this customer's basket"; every write replaces it wholesale. Not one query joins, filters or aggregates, so nothing a relational database is good at is being used. |
| **The data is disposable** | Losing a basket is an annoyance; losing an order is an incident. Accepting weaker durability for basket data is a legitimate trade — and one you can only make if baskets are not sitting in the orders database. |
| **Expiry is built in** | Abandoned baskets should disappear. Redis does that with a TTL. PostgreSQL needs a scheduled job somebody has to write, run and monitor. |

**The counterweight, stated honestly:** this is another technology to operate, back up and understand. For
a smaller system a `baskets` table would be a perfectly reasonable answer, and "we already run PostgreSQL"
is a real argument. The point is not that Redis is better — it is that the choice is *available* per
service, and here the data genuinely fits it.

### Storage details

```
Key:   basket:{sub}          -- namespaced, because Redis is one flat keyspace shared by everything
Value: JSON
TTL:   30 days, refreshed on every write
```

The **prefix** stops this service's keys colliding with a cache entry someone adds next year, and makes
`SCAN basket:*` possible when diagnosing.

The **TTL** is a product decision, not a technical one: long enough that a customer returning next week
finds their basket, short enough that Redis is not storing years of abandoned intentions. Refreshed on
every write, so an active basket never expires underneath someone.

### `ConnectionMultiplexer` is a singleton

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));
```

It is designed to be shared for the life of the application: it multiplexes every command over a small
number of sockets. Creating one per request — the reflex, because it is called *Connection* — opens a
socket per request, exhausts the pool under load, and is the single most common way to make Redis look
slow.

### An unreadable basket is discarded, not an error

```csharp
catch (JsonException ex)
{
    logger.LogWarning(ex, "Discarding unreadable basket for {BuyerId}.", buyerId);
    return null;
}
```

A basket written by an older version whose shape no longer deserialises is treated as **absent**. A
customer with an empty basket can carry on shopping; a 500 on every page load cannot be worked around.
Logged at warning rather than swallowed, because if this happens often the schema change was not backward
compatible and someone needs to know.

---

## The prices in a basket are not a promise

**This is the single most important thing on this page.**

The client sends the name and price it is displaying when adding an item. That is safe *here*, because
nothing in a basket is binding — and it is emphatically **not** safe at checkout.

When an order is placed, [Ordering re-derives every price from Catalog](ordering.md#re-pricing). The
basket's price is used for exactly one thing: showing the customer a total before they commit.

Verified by attacking it. A basket claiming a £14.50 mug costs £0.01:

```bash
curl -s -X POST "$BFF/api/basket/me/items" -H "Authorization: Bearer $TOKEN" \
  -d '{"productId":"...","sku":"FB-DW-003","productName":"Ceramic Mug","unitPrice":0.01,"quantity":1}'
# basket claims total: 0.01

curl -s -X POST "$BFF/api/orders" -H "Authorization: Bearer $TOKEN" -d @order.json
# order: ORD-20260726-9917 | server total: 14.50 GBP
```

> The rule generalises well beyond prices: **anything that ends up in a ledger is derived server-side,
> never accepted from a client.** A client-supplied price that reaches the ledger is a discount anyone can
> grant themselves.

The basket page says so in plain words rather than hiding it in small print — *"Prices are confirmed when
you place your order, so this total may change"* — and an e2e spec asserts that sentence is present.

---

## Endpoint reference

Base path through the BFF: `/api/basket`.

Every route is `/me`, resolved server-side from the `sub` claim. There is no `/baskets/{buyerId}` to
tamper with.

| Method | Route | Permission | Purpose |
|--------|-------|-----------|---------|
| `GET` | `/me` | `basket:read:own` | The caller's basket |
| `POST` | `/me/items` | `basket:write:own` | Add, or increase the quantity if already present |
| `PUT` | `/me/items/{productId}` | `basket:write:own` | Set a quantity. **0 removes the line.** |
| `DELETE` | `/me/items/{productId}` | `basket:write:own` | Remove a line |
| `DELETE` | `/me` | `basket:write:own` | Empty the basket |

Both permissions are held by **every** signed-in role — see
[the authorization model](../authorization-model.md#4-the-matrix--which-role-grants-what).

### Internal routes, not exposed through any BFF

| Method | Route | Called by |
|--------|-------|-----------|
| `GET` | `/internal/basket/{buyerId}` | Ordering, when placing an order |
| `DELETE` | `/internal/basket/{buyerId}` | Ordering, once the order exists |

These take a buyer id in the path — which would be a serious flaw on a public route and is acceptable here
because the only way to reach them is from inside the container network.

> **That is network-level trust, and it is worth being honest about.** It is adequate for this repo and
> insufficient for a real deployment: anything that gets into the network can call them. The production
> answer is mutual TLS, a service mesh, or a service-to-service token with its own audience. Recorded here
> rather than left as an unexamined assumption.

### Behaviours worth knowing

**An absent basket is an empty basket, not a 404.** "You have no basket yet" and "your basket is empty" are
the same thing to a customer, and a 404 would force every client to write the same special case.

**Quantity 0 removes the line.** A stepper that reaches 0 should empty the line rather than leave a row
showing "0" that every client then has to filter out.

**Adding beyond the limit clamps rather than fails.** Someone with 95 who adds 10 more clearly wants "as
many as I can have"; an error about a limit they did not know existed is a worse outcome.

**Every mutating endpoint returns the whole basket.** No `204`, no guessing what changed, no second call to
find out.

---

## What it deliberately does not do

| Not here | Why |
|----------|-----|
| **Anonymous baskets** | A real feature and a real complication: it needs a cookie-scoped identity and a merge strategy for the moment a guest signs in holding items. Out of scope, and named as a decision rather than left as an apparent oversight. |
| **Stock checking** | The basket does not reserve anything. Stock is checked when the order is placed, because reserving on add would hold inventory for every abandoned basket in the shop. |
| **Price recalculation** | Prices refresh when an item is re-added, and are otherwise left alone. The authoritative check happens once, at checkout, where it matters. |
| **Publishing events** | Nothing outside cares that a basket changed. An event nobody consumes is cost without benefit. |

---

## Configuration

| Key | Default (compose) | Notes |
|-----|-------------------|-------|
| `ConnectionStrings__Redis` | `redis:6379,password=…` | From `deploy/.env`; never committed |
| `Auth__Authority` | `http://keycloak:8080/realms/ecommerce` | |
| `Auth__Audience` | `ecommerce-api` | |

## Health

| Probe | Route | Checks |
|-------|-------|--------|
| Liveness | `/health/live` | Process is up |
| Readiness | `/health/ready` | Redis reachable |

## Testing

Covered by [`tests/e2e/specs/shopping.spec.ts`](../../tests/e2e/specs/shopping.spec.ts) — add, quantity
change, zero-removes, empty state and the price warning, run against **both** storefronts.
