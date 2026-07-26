# A hands-on guide to the four tools

> **Assumes no prior knowledge of any of them.** If you already use Seq and Jaeger daily, skip to
> [observability.md](observability.md) for how *this* system wires them up.
>
> Everything here is something you can do right now against the running stack. Start it first:
> `cd deploy && docker compose up -d --wait`

Four tools ship with this system. None of them is written by us, none is part of .NET, and each runs as its
own container with its own web UI:

| Tool | Port | One sentence |
|------|------|--------------|
| [**Seq**](#1-seq--reading-the-logs) | http://localhost:8081 | Search your logs like a database instead of grepping text |
| [**Jaeger**](#2-jaeger--following-a-request-across-services) | http://localhost:16686 | See where the time went when one request crosses several services |
| [**RabbitMQ**](#3-rabbitmq--the-message-broker) | http://localhost:15672 | Watch services talk to each other without calling each other |
| [**Keycloak**](#4-keycloak--logins-users-and-permissions) | http://localhost:8080 | Handles logins, users, roles and tokens so our code never touches a password |

The first two are for **debugging**. The last two are **running parts of the system**.

---

## Why these exist at all

In a single application, debugging is: read the log file, attach a debugger. Both work because there is one
process and one machine.

This system has **nine services in nine containers**. One customer clicking "Buy" will touch five of them.
When it fails:

- *Which service failed?* — nine log streams to check.
- *Was it slow, or actually broken?* — no single place shows total elapsed time.
- *Which log lines belong to my request?* — they are interleaved with everyone else's.

That is what these tools solve. **This is not optional tooling for a distributed system; it is the
difference between debuggable and not.** "I'd SSH in and check the logs" is an answer that does not survive
a follow-up question in an interview.

---

# 1. Seq — reading the logs

**http://localhost:8081** · no login in development

## The idea: structured logging

A traditional log line is a **string**:

```
2026-07-25 14:32:01 INFO Order 12345 rejected for customer 987 - insufficient stock
```

To find all rejected orders you grep for `"rejected"`. To find them for one customer you write a regex. To
count them per day you write a script. The information is in there, but only as text.

**Structured logging** stores the same event as **data with named fields**:

```json
{
  "Message": "Order 12345 rejected for customer 987 - insufficient stock",
  "OrderId": 12345,
  "CustomerId": 987,
  "Reason": "insufficient stock",
  "ServiceName": "ordering",
  "CorrelationId": "abc-123",
  "TraceId": "4bf92f3577b34da6"
}
```

Now "all rejected orders for customer 987 today" is a **query**, not a regex. That is the entire point of
Seq: it stores the fields, so you can search them.

In our code this comes from **Serilog**, and it is why log calls look like this:

```csharp
_logger.LogWarning("Order {OrderId} rejected for {CustomerId}: {Reason}", orderId, customerId, reason);
```

Those `{OrderId}` placeholders are not string formatting — each becomes a **searchable field**. This is why
you should never write `$"Order {orderId} rejected"` with string interpolation: it produces the same text
but throws the structure away, and Seq can no longer search it.

## Try it now

**1. Generate some log events.** In a terminal:

```bash
curl -H "X-Correlation-Id: my-first-test" http://localhost:5001/
curl -H "X-Correlation-Id: my-first-test" http://localhost:5003/
```

**2. Open http://localhost:8081.** You will see a live list of events. Every service in the stack is writing
here.

**3. Search for yours.** Paste into the filter box at the top and press Enter:

```
CorrelationId = 'my-first-test'
```

Two events — one from catalog, one from ordering. **You just found every log line belonging to one request,
across two separate services.** That is the thing that is hard without this.

**4. Click an event to expand it.** You will see the fields attached: `ServiceName`, `CorrelationId`,
`TraceId`, `SpanId`, `RequestPath`, `StatusCode`, `Elapsed`. These are what you can query on.

## Queries worth knowing

Seq's filter language looks like C#, which is deliberate:

```sql
ServiceName = 'ordering'                          -- one service
@Level = 'Error'                                  -- errors only
@Level in ['Error', 'Fatal']                      -- errors and worse
StatusCode >= 500                                 -- server failures
Elapsed > 1000                                    -- slow requests (ms)
CorrelationId like 'my-%'                         -- pattern match
ServiceName = 'ordering' and @Level = 'Error'     -- combine with and/or
@Exception is not null                            -- anything that threw
```

`@Level`, `@Exception`, `@Timestamp` are Seq's built-ins (the `@` marks them); everything else is a field our
code attached.

## Things worth clicking

- **The time range selector** (top right) — default is last hour.
- **Any field value in an expanded event** → *"Find similar"*. Fastest way to build a query without typing.
- **Signals** (left sidebar) — a saved query. Make one for `@Level = 'Error'` and it becomes a one-click
  filter.

## What you'll use it for later

From Phase 6, when an order fails, you will paste its correlation id here and get every log line from all
five services involved, in order, as one list.

---

# 2. Jaeger — following a request across services

**http://localhost:16686** · no login

## The idea: traces and spans

Seq tells you **what the code said**. Jaeger tells you **where the time went**.

Two words to learn:

- A **span** is one unit of work with a start and end time — "Inventory reserved stock, 240ms".
- A **trace** is all the spans belonging to one user action, linked into a tree.

A trace for placing an order will eventually look like this:

```
POST /orders                                    [============================] 1,240ms
├─ ordering: create order                       [====]                            180ms
├─ saga: reserve stock                             [======]                       240ms
│   └─ inventory: check + reserve                   [====]                        190ms
├─ saga: take payment                                    [==============]         710ms   ← the slow one
│   └─ payment: call gateway                              [=============]         680ms
└─ ordering: confirm                                                   [==]        90ms
```

At a glance: the request took 1.24 seconds and **the payment gateway is 55% of it**. No amount of log
reading gives you that shape — you would be manually subtracting timestamps across five services.

## Try it now

**1. Generate traffic:**

```bash
curl http://localhost:5001/
curl http://localhost:5003/
```

**2. Open http://localhost:16686.**

**3. In the left panel:** choose `catalog` from the **Service** dropdown → click **Find Traces**.

**4. Click any trace.** You will see a waterfall. Right now each is a single short span, because Phase 1
services only answer for themselves — there is nothing to fan out to yet. The structure is what matters;
Phase 7 fills it in.

**5. Click the span** to expand its **tags**: HTTP method, route, status code, and the service that produced
it.

## Reading a waterfall

- **Bar length = duration.** The longest bar is your problem.
- **Bar position = when it started.** Bars side by side ran in parallel; staircased bars ran sequentially —
  and a long staircase is often work that *could* have been parallel.
- **Nesting = causation.** A child span was caused by its parent.
- **A red span** threw an exception. Click it for the stack trace.

## The hard part this solves

A message broker normally **breaks** a trace. When Ordering publishes an event and Inventory picks it up
later, they are separate processes with no shared call stack — automatic instrumentation sees two unrelated
operations.

We fix that by carrying the `traceparent` on the message itself, so the consumer's span re-attaches to the
original trace. That is what [`IntegrationEvent.TraceParent`](../../src/building-blocks/EventBus/IntegrationEvent.cs)
is for, and it is why a single trace will span asynchronous hops from Phase 7.

## Using Seq and Jaeger together

This is the actual workflow, and the reason both exist:

1. Jaeger: *"the payment span took 3 seconds"* — you know **where**.
2. Copy the `TraceId` from that span.
3. Seq: `TraceId = '<paste>'` — you know **why**, from what the code logged.

That pivot works because Serilog stamps `TraceId` onto every log line. The `Enrich.WithSpan()` line in
[`ObservabilityExtensions.cs`](../../src/building-blocks/Observability/ObservabilityExtensions.cs) exists
solely to make it possible.

---

# 3. RabbitMQ — the message broker

**http://localhost:15672** · `ecom` / `dev_only_rabbit_pw`

## The idea: talking without calling

If Ordering **calls** Inventory directly over HTTP, then Ordering only works when Inventory works. Chain a
few of those together and one service being down takes the whole system with it.

With a broker, Ordering **publishes a message** and carries on. Inventory picks it up whenever it is ready —
now, or in ten minutes after a restart. Nothing is lost and nobody waits.

Three words:

- **Exchange** — where publishers send. Ours is `ecommerce.events`.
- **Queue** — where messages wait for a consumer.
- **Binding** — a rule connecting an exchange to a queue ("send me anything named `OrderStarted`").

Publishers know nothing about subscribers. Adding a fifth service that reacts to orders requires **no change
to Ordering** — it just binds a new queue.

## Try it now

Open http://localhost:15672 and log in.

- **Overview** — message rates. Flat, because nothing publishes until Phase 6.
- **Exchanges** — `ecommerce.events` appears once a service declares it.
- **Queues** — empty for now. This becomes the most useful tab in the system.

## What this tab will show you later

From Phase 6, the *Queues* tab is genuinely the best demo in the repository:

1. Place an order → watch **Ready** count tick up as messages appear.
2. `docker compose stop inventory-api` → place another order → **messages pile up safely** instead of the
   order failing.
3. `docker compose start inventory-api` → watch the queue **drain**.

That is fault tolerance you can see, and it is the concrete answer to *"what happens if a service is down?"*

Also watch for queues ending **`.dlq`** — the *dead-letter queue*. A message that fails repeatedly lands
there instead of being retried forever. Anything in a DLQ is a message that needs a human.

---

# 4. Keycloak — logins, users and permissions

**http://localhost:8080** · `admin` / `dev_only_kc_admin_pw`

## The idea: don't build your own login

Keycloak owns passwords, password hashing, MFA, password reset, sessions, and token issuance. **Our services
never see a password.** When you log into the storefront, the browser redirects to Keycloak, you type your
password *there*, and Keycloak hands back a signed token.

Why not build it ourselves? Because you would own password hashing, brute-force protection, account
recovery, token signing and rotation — forever, correctly, with no competitive advantage. And "we wrote our
own auth" is a finding in every security review. Full argument:
[ADR-0005](../adr/0005-keycloak-as-identity-provider.md).

## Vocabulary

| Term | Meaning |
|------|---------|
| **Realm** | An isolated world of users, roles and apps. Ours will be `ecommerce`. |
| **Client** | One application that can request tokens. We will have one per app — `storefront-react`, `admin-angular`, and so on. |
| **Realm role** | A coarse identity: `customer`, `admin`, `catalog-manager`. |
| **Client role** | A fine-grained permission: `catalog:write`, `order:refund`. |
| **Composite role** | A role that grants other roles — how `catalog-manager` acquires `catalog:write`. |
| **Token (JWT)** | A signed piece of JSON proving who you are and what you may do. Sent on every API call. |

## Try it now

Log in and click **Manage realms** (top left). Only `master` exists — that is Keycloak's own admin realm.
The `ecommerce` realm arrives in **Phase 2**, imported automatically from a JSON file committed to this repo
(no clicking through the console to set it up).

Meanwhile you can see the machinery is live:

```bash
curl http://localhost:8080/realms/master/.well-known/openid-configuration
```

That is the **OIDC discovery document** — the standard endpoint every OAuth2 client reads to learn where to
send users to log in, and where to fetch the public keys for verifying token signatures. Our services read
it at startup so they can validate tokens offline, with no call to Keycloak per request.

## What Phase 2 adds

The `ecommerce` realm, the full role and permission model, and **seed users you can actually log in as** —
a customer, a catalog manager, an order manager, a support agent, and an admin, with their passwords listed
in the README.

---

# Cheat sheet

| I want to… | Go to |
|------------|-------|
| See what the code logged | **Seq** — http://localhost:8081 |
| Find every log line for one request | **Seq** — `CorrelationId = '...'` |
| Find out why something was slow | **Jaeger** — http://localhost:16686 |
| See which service failed in a chain | **Jaeger**, then the red span |
| Check whether messages are flowing | **RabbitMQ** — *Queues* tab |
| Find messages that failed repeatedly | **RabbitMQ** — queues ending `.dlq` |
| Manage users, roles or permissions | **Keycloak** — http://localhost:8080 |
| Check a service is alive | `curl http://localhost:5001/health/live` |
| Check its dependencies are reachable | `curl http://localhost:5001/health/ready` |

## If a tool looks empty

That is expected at Phase 1, not a fault:

- **Seq empty?** Nothing has been called. `curl http://localhost:5001/` then refresh.
- **Jaeger has no services?** Same — traces appear after the first request.
- **RabbitMQ has no queues?** They are declared when a service subscribes, so they appear once the stack
  has finished booting. Place an order and you will see `ordering`, `inventory`, `payment`,
  `notification` and `ordering-saga` queues bound to the `ecommerce.events` exchange, each with its own
  `.dlq`.
- **Keycloak has only `master`?** The `ecommerce` realm is imported on Keycloak's FIRST start only. If it
  is missing, the container has an old volume: `docker compose rm -sf keycloak keycloak-db`,
  `docker volume rm ecommerce_keycloak-db-data`, then bring it up again — and restart the services
  afterwards, because a re-imported realm has new signing keys.

## Are we locked into these?

No, and that is deliberate. Our services emit **standard formats** — OpenTelemetry (OTLP) for traces and
metrics, Serilog for logs. Jaeger and Seq simply happen to be what is listening. Changing one URL in
configuration sends the same data to Grafana Tempo, Azure Application Insights, Datadog, or Elastic, with
**no code change**.

These two were chosen because they run offline in a container with no account, no API key, and no cost —
which matters for a repository anyone should be able to clone and run.
