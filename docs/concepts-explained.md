# Every concept in this system, in plain English

> **Read this first if the words in this repository are unfamiliar.** No jargon assumed. Every idea is
> explained with an everyday analogy, then what it means here, then where the code lives.
>
> The [concept map](concept-map.md) covers the same ground for someone who already knows the terms and wants
> the interview answer. This page is the one to start with.

**How to read this page.** It builds up in order — later sections assume the earlier ones. If you only read
three, read [What is a microservice](#1-what-is-a-microservice), [What is a BFF](#6-what-is-an-api-gateway-and-a-bff),
and [What is a saga](#12-what-is-a-saga).

---

## Table of contents

**The big picture**
1. [What is a microservice](#1-what-is-a-microservice)
2. [What is a bounded context](#2-what-is-a-bounded-context)
3. [Why services never share a database](#3-why-services-never-share-a-database)

**How services talk**
4. [Synchronous vs asynchronous](#4-synchronous-vs-asynchronous)
5. [What is REST and what is gRPC](#5-what-is-rest-and-what-is-grpc)
6. [What is an API gateway, and a BFF](#6-what-is-an-api-gateway-and-a-bff)
7. [What is a message broker](#7-what-is-a-message-broker)
8. [What is an integration event](#8-what-is-an-integration-event)

**Keeping data correct**
9. [What is eventual consistency](#9-what-is-eventual-consistency)
10. [What is idempotency](#10-what-is-idempotency)
11. [What is the outbox pattern](#11-what-is-the-outbox-pattern)
12. [What is a saga](#12-what-is-a-saga)

**Organising the code**
13. [What is DDD](#13-what-is-ddd-domain-driven-design)
14. [Entity, value object, aggregate](#14-entity-value-object-aggregate)
15. [What is CQRS](#15-what-is-cqrs)
16. [Repository, Unit of Work, Mediator](#16-repository-unit-of-work-mediator)

**Security**
17. [OAuth2, OIDC, JWT and PKCE](#17-oauth2-oidc-jwt-and-pkce)
18. [Roles vs permissions](#18-roles-vs-permissions)

**Keeping it running**
19. [Retry, circuit breaker, timeout](#19-retry-circuit-breaker-timeout)
20. [Health checks](#20-health-checks)
21. [Logging, tracing, correlation IDs](#21-logging-tracing-correlation-ids)
22. [Docker and Docker Compose](#22-docker-and-docker-compose)

---

# The big picture

## 1. What is a microservice

**The analogy.** Think of a restaurant. One option is a single person who takes orders, cooks, washes up and
handles the till. That is a **monolith** — one program doing everything. The other is a team: a waiter, a
chef, a dishwasher, a cashier. Each does one job, each can be replaced or given help independently. That is
**microservices**.

**What it means here.** Instead of one big "shop" program, we have nine small programs, each owning one part
of the business:

| Service | Its one job |
|---------|-------------|
| Catalog | Knows about products — names, prices, pictures |
| Basket | Knows what is in your shopping cart |
| Ordering | Knows about orders you have placed |
| Payment | Takes money |
| Inventory | Knows how many of each item are in the warehouse |
| Notification | Sends emails |
| User Profile | Knows your name, addresses and preferences |
| Ordering Saga | Coordinates the steps of placing an order |
| Back-office | Powers the staff admin screens |

Each runs in its own container, has its own database, and can be updated or restarted without touching the
others.

**Why bother?** Three real reasons: you can deploy one service without redeploying everything; you can scale
just the busy part (browsing gets far more traffic than checkout); and if Ordering crashes, people can still
browse.

**The honest catch.** Microservices are *harder*, not easier. You swap simple problems (a function call) for
hard ones (a network call that might fail). Most of the other concepts on this page exist purely to manage
that difficulty. For a shop this size **a monolith would genuinely be the better choice** — this repo uses
microservices because its purpose is to demonstrate the patterns. That argument is written out in full in
[ADR-0002](adr/0002-microservices-over-modular-monolith.md).

---

## 2. What is a bounded context

**The analogy.** In a hospital, the word "patient" means different things to different departments. To
Billing, a patient is an account and an insurance number. To the Ward, it is a bed, a chart and a diet. To
Records, it is a medical history. Nobody tries to build one giant "Patient" form that satisfies all three —
it would be enormous and please nobody.

A **bounded context** is the boundary inside which a word has one clear meaning.

**What it means here.** In our shop, "customer" means three different things:

| Where | What "customer" means |
|-------|----------------------|
| Keycloak (login system) | A username, a password, and a list of roles |
| User Profile service | A name, saved addresses, preferred language |
| Ordering service | A customer ID and **the address frozen at the moment they bought** |

So we do **not** build one shared `Customer` class. We build three small models, each complete for its own
job, linked by a single shared ID.

**How to spot a boundary:** when the same word means different things to different people, you have found
one. That is genuinely the main test.

📄 [`docs/domain/bounded-contexts.md`](domain/bounded-contexts.md)

---

## 3. Why services never share a database

**The analogy.** Two housemates sharing one diary. If one rearranges the pages, the other can no longer find
anything. Neither can reorganise without a negotiation. They are stuck moving at the same speed.

**The rule here:** every service owns its own database, and **no service ever reads another's tables.**

If Ordering read directly from Catalog's product table, then Catalog could never rename a column without
breaking Ordering. You would have all the pain of separate services with none of the independence — a
"**distributed monolith**", which is the worst outcome available.

**What you give up.** In a normal database you can `JOIN` two tables and let a foreign key guarantee the
product actually exists. Across services you cannot. Instead:

- **Ask at the time** — when creating an order, Ordering asks Catalog "is this product real, and what does it
  cost?"
- **Copy what you need** — the order stores the product's *name and price at that moment*. This is not
  laziness; it is correct. Your receipt should not change when the shop changes its prices next week.

📄 [`docs/architecture.md §7`](architecture.md#7-data-sovereignty-why-services-never-share-a-database)

---

# How services talk

## 4. Synchronous vs asynchronous

**The analogy.** **Synchronous** is a phone call — you ask, you wait on the line, you get an answer. If they
do not pick up, you are stuck. **Asynchronous** is a text message — you send it and get on with your day.
They reply when they can.

**Why it matters.** Phone calls chain badly. If Ordering phones Inventory, and Inventory phones Catalog, then
Ordering only works when *all three* are up. Chain enough and something is always broken.

**Our rule: text messages by default; phone calls only when you genuinely cannot continue without the
answer.**

| Situation | Which | Why |
|-----------|-------|-----|
| Shopper is staring at a loading spinner | Phone call | There is no "later" — they are waiting |
| "What does this product cost right now?" at checkout | Phone call | Must be right *this second* |
| "An order was placed" | Text message | Nobody is waiting; Inventory and Email can catch up |
| "Stock levels changed" | Text message | The product page can be a few seconds stale |

📄 [`docs/architecture.md §6`](architecture.md#6-communication-choosing-synchronous-or-asynchronous)

---

## 5. What is REST and what is gRPC

Both are ways for two programs to talk over a network. They are two different "languages".

**REST** sends **JSON** — human-readable text — over ordinary web requests:

```http
GET /products/42
→ { "id": 42, "name": "Blue T-Shirt", "price": 19.99 }
```

You can read it, test it in a browser, and use `curl`. Every browser speaks it natively.

**gRPC** sends **binary** data described by a contract file (`.proto`):

```proto
service Catalog {
  rpc GetProduct (ProductRequest) returns (ProductReply);
}
```

Smaller and faster, and — the big win — **the contract is checked by the compiler**. If Catalog removes a
field that Ordering uses, the *build breaks* instead of production breaking. The downside: you cannot read
it by eye, and browsers cannot speak it.

**So we use both, in the right places:** gRPC between services (where compiler-checked contracts are
valuable), REST at the edge where browsers connect (where readability and browser support matter).

📄 [ADR-0007](adr/0007-grpc-for-internal-sync-calls.md)

---

## 6. What is an API gateway, and a BFF

**The problem.** A product page needs product details, stock level, and price — from three different
services. Should the browser make three calls and know all three addresses? No: that hard-codes our internal
structure into the website, and any reorganisation breaks every client.

**An API gateway** is a single front door. Clients talk only to it, and it talks to the services behind.
Like a hotel receptionist — you ask them, they deal with housekeeping, the kitchen and maintenance on your
behalf.

**So what is a BFF?** **Backend For Frontend** — one gateway *per type of client*, instead of one shared by
everybody.

**Why?** Because different clients genuinely need different things:

| Client | What it needs |
|--------|---------------|
| Website storefront | Big rich product pages, heavily cached, public |
| Admin panel | Every action permission-checked and audit-logged |
| Mobile app | Fewer, smaller responses — the user is on 4G with a battery |

If one gateway served all three, it would fill with `if (mobile) ... else if (admin) ...`, every team's
changes would collide in one file, and a change for mobile would risk breaking the admin panel. So we have
three:

- `storefront-bff` → serves the shop website
- `admin-bff` → serves the staff admin panel
- `mobile-bff` → serves the phone app

**One subtlety worth understanding:** the React storefront and the Angular storefront **share one BFF**.
A BFF exists per *user experience*, not per *technology*. Both storefronts show the same shop, so they need
identical data. This also proves they behave identically — since both call the same endpoints, any
difference between them must be a bug in the UI code.

📄 [ADR-0006](adr/0006-yarp-gateway-and-bff-per-client.md) · code in [`src/gateways/`](../src/gateways/)

---

## 7. What is a message broker

**The analogy.** A post office. You do not hand a letter directly to your friend — you drop it off, and the
post office delivers it whenever they can. If your friend is on holiday, the letter waits. Neither of you
has to be available at the same moment.

**RabbitMQ** is our post office. Ordering does not call Inventory. It posts a message saying *"an order was
placed"*, and Inventory picks it up when it is ready.

**Three words you will see:**

| Word | Meaning |
|------|---------|
| **Exchange** | The postbox you drop messages into |
| **Queue** | The mailbox where messages wait for one recipient |
| **Binding** | The forwarding rule — "send me anything about orders" |

**Why this is a big deal.** Restart Inventory for ten minutes and **nothing is lost**. Messages queue up and
get processed on return. With a phone call, every order during those ten minutes would have failed.

It also means the sender does not know or care who is listening. Adding a fifth service that reacts to
orders needs **zero changes** to Ordering.

You can watch this happen: http://localhost:15672 (`ecom` / `dev_only_rabbit_pw`).

📄 [ADR-0016](adr/0016-rabbitmq-behind-ieventbus.md) · [tooling guide](operations/tooling-guide.md#3-rabbitmq--the-message-broker)

---

## 8. What is an integration event

An **integration event** is one of those messages: an announcement that **something has already happened**.

**The naming rule: always past tense.** `OrderPlaced`, not `PlaceOrder`. This is not pedantry — it reflects
who is in charge:

- **An event** says *"this happened."* Listeners may react. They cannot refuse. It is already true.
- **A command** says *"do this."* One recipient, and it can be rejected.

**Why past tense matters in practice:** if Ordering sent "PlaceOrder" as a command, it would need to know who
executes it and what happens if they say no. By announcing "OrderPlaced", Ordering is finished — its job is
done, and anyone who cares can react. That is what keeps services independent.

**The critical rule: an event is a public promise.** Once another service listens to it, changing its shape
breaks *their* code. So you may **add** optional fields, but never remove or rename one.

📄 [`docs/events/event-catalogue.md`](events/event-catalogue.md)

---

# Keeping data correct

## 9. What is eventual consistency

**The analogy.** You update your address with your bank. The app shows it immediately. The letter they post
next week still goes to the old address, because that system has not caught up yet. Eventually everything
agrees. In between, different parts of the bank know different things.

**In our shop:** you place an order. Ordering knows instantly. Inventory finds out a second later, when it
picks up the message. For that second, the two disagree.

**This sounds bad. It is usually fine — but only if you choose carefully where.**

| Situation | Stale data OK? | Why |
|-----------|:---:|-----|
| Product page says "3 left" but it is really 2 | ✅ | Nobody is harmed; we check properly at checkout |
| Order total | ❌ | Must be exactly right, always |
| Reserving stock when you actually buy | ❌ | Overselling costs real money |

**The skill is knowing which is which.** Product-page stock can be stale, because the real check happens at
the moment of reservation. Being wrong there is not acceptable — so that operation is *not* eventually
consistent.

---

## 10. What is idempotency

**The word.** It sounds academic; the idea is simple. Something is **idempotent** if doing it twice has the
same effect as doing it once.

- Pressing a lift button ten times = pressing it once. **Idempotent.**
- Adding £10 to a balance ten times ≠ once. **Not idempotent.**

**Why we care.** Message delivery cannot be perfect. If a service processes a message and crashes *before*
confirming it, the broker sensibly re-delivers it. So **every message will occasionally arrive twice.** That
is normal, not a bug.

If "PaymentSucceeded" is handled twice and it is not idempotent, **you charge the customer twice.** That is
the actual stakes.

**How we fix it.** Every message carries a unique ID. Before acting, a service checks "have I already seen
this ID?" — and skips it if so. That check is saved in the same database transaction as the work itself, so
the two can never disagree.

> **A common exam question:** *"Can you guarantee a message is delivered exactly once?"*
> **No — that is impossible** over a network. What you do is deliver *at least once* and make handlers
> idempotent. The combination gives the same result.

📄 [`IIntegrationEventHandler.cs`](../src/building-blocks/EventBus/IIntegrationEventHandler.cs)

---

## 11. What is the outbox pattern

**The problem, in one picture.** When an order is placed, two things must happen:

1. Save the order to our database
2. Post a message saying "OrderPlaced"

These are two different systems. There is no way to do both as one all-or-nothing operation. So:

```
Save the order       ✅ worked
Post the message     ❌ crashed here
```

The order now exists and **nothing is fulfilling it**. It sits forever. Nobody gets an error. Nobody notices
until the customer complains. This is called the **dual-write problem**, and it is the most dangerous kind of
bug: silent.

**The fix — a brilliantly simple trick.** Do not post the message. Instead, write the message **into your
own database**, in the very same transaction as the order:

```
BEGIN
  save the order
  save "need to send: OrderPlaced" into an outbox table
COMMIT          ← both, or neither. Guaranteed.
```

Then a small background job reads the outbox table and posts the messages, ticking each off once the broker
confirms receipt.

**Why it works:** there is now only *one* system involved in the risky step, and databases are very good at
all-or-nothing. If the background job crashes, the message is still sitting in the table waiting.

**The catch:** if it crashes *after* posting but *before* ticking it off, the message gets sent twice —
which is exactly why [idempotency](#10-what-is-idempotency) is mandatory. **These two patterns are two halves
of one solution.** Doing the outbox without idempotent handlers just moves the bug to somewhere more
expensive.

📄 [ADR-0010](adr/0010-transactional-outbox.md)

---

## 12. What is a saga

**The problem.** Placing an order means several steps across several services:

1. Create the order (Ordering)
2. Reserve the stock (Inventory)
3. Take the payment (Payment)
4. Confirm the order (Ordering)

In a single database you would wrap these in a transaction — if step 3 fails, everything rewinds
automatically. **Across services, there is no such rewind.** If payment fails at step 3, the stock from
step 2 is still reserved, and those items are now unsellable forever.

**A saga is a sequence of steps where each step has an "undo" step**, and if something fails partway, the
undos run in reverse.

**The crucial insight: the undo is a business action, not a technical rewind.** You cannot un-charge a card —
you issue a refund. You cannot un-send an email — you send a correction. These are called **compensating
actions**.

**Our order flow, both ways it can go:**

```
Happy path:
  Reserve stock ✅ → Take payment ✅ → Confirm order ✅

Payment declined:
  Reserve stock ✅ → Take payment ❌ → RELEASE the stock ↩ → Cancel the order ↩
```

**Why we reserve stock *before* taking payment.** A general rule worth remembering: **order the steps so the
most likely failure has the cheapest undo.** Payments fail often; releasing a stock reservation is harmless.
If we charged first, a stock failure would mean refunding a customer for goods we do not have — a support
call and a trust problem.

**Two ways to build one:**

| | **Choreography** | **Orchestration** ← *we use this* |
|---|---|---|
| How | Each service listens for the previous one's event and reacts | One coordinator tells each service what to do |
| Analogy | Dancers who each know their cue | A conductor directing the orchestra |
| Good | No central point of failure | The whole process is in one readable file |
| Bad | **The process exists nowhere** — to understand it you must read six services | The coordinator must know about everyone |

We chose orchestration because with choreography, nobody can answer "what actually happens when an order is
placed?" without reading the entire codebase — and undo logic gets smeared across every service until
Inventory needs to know about payment failures.

📄 [ADR-0011](adr/0011-orchestration-saga.md) · code in [`src/services/ordering-saga/`](../src/services/ordering-saga/)

---

# Organising the code

## 13. What is DDD (Domain-Driven Design)

**The idea in one sentence:** write code that uses the same words the business uses, and put the business
rules inside the objects they belong to.

**The bad way** — objects are dumb bags of data, and the rules live scattered in whatever code happens to
touch them:

```csharp
order.Status = "Cancelled";   // Nothing stopped me. Even if it already shipped.
```

**The DDD way** — the object protects itself:

```csharp
order.Cancel();   // Throws if the order already shipped. The rule lives WITH the order.
```

Now the rule "you cannot cancel a shipped order" exists in exactly **one** place and cannot be bypassed. Add
a new screen, a new API, an import script — all of them go through `Cancel()` and all of them obey the rule.

**The other half of DDD is language.** If the business says "basket", the class is `Basket`, not
`ShoppingCartEntity`. It sounds trivial; it prevents an entire category of misunderstanding between the
people who know the rules and the people writing them down.

**We do not do this everywhere.** Ordering gets the full treatment because it has real rules. Basket is
essentially a list in a box and gets simple code. **Spend the complexity where the business is complex.**

---

## 14. Entity, value object, aggregate

Three DDD words for three kinds of object.

### Entity — has an identity

Something that stays "the same thing" even as its details change. **You** are an entity: change your name,
address and hair colour, and you are still you.

Order #123 is an entity. Update the address, it is still order #123.

👉 Two entities are equal **if their IDs match**, regardless of their contents.

### Value object — is just its value

Something with no identity, defined entirely by its contents. A **£10 note** is a value object — swap it for
another £10 note and nothing has changed.

`Money(10, "GBP")` is a value object. So is an address.

👉 Two value objects are equal **if their contents match**.

**Why bother?** Because it makes the computer catch mistakes:

```csharp
decimal a = 10;  // pounds
decimal b = 15;  // dollars
var total = a + b;      // 😱 compiles fine. Produces nonsense.

Money a = new(10, "GBP");
Money b = new(15, "USD");
var total = a + b;      // ✅ refuses — different currencies
```

Also, a value object validates itself once when created, so **if you are holding one, it is valid** — no
defensive checking anywhere else.

### Aggregate — a group with a guard

An **aggregate** is a cluster of objects treated as one unit, with one object in charge — the **aggregate
root**. All access goes through the root.

Our `Order` aggregate is `Order` (the root) plus its `OrderItem` lines. You may never fetch or modify an
`OrderItem` directly — you go through the `Order`. That is precisely what lets `Order` guarantee rules like
"the total always equals the sum of the lines".

**How big should an aggregate be?** Include only what must *always* be correct together, instantly.

- Order + its lines → **same aggregate** (a total that disagrees with its lines is nonsense)
- Order + the customer → **different aggregates** (an order does not break if a customer edits their phone
  number)

Different aggregates refer to each other **by ID only**, never by holding the object.

📄 [`src/building-blocks/Common/SeedWork/`](../src/building-blocks/Common/SeedWork/)

---

## 15. What is CQRS

**The name:** Command Query Responsibility Segregation. The idea is much simpler than the name.

**Separate the code that changes data from the code that reads data.**

**Why?** Because they want opposite things:

| | Writing | Reading |
|---|---|---|
| Needs | Rules, validation, safety | Speed |
| Example | "Place this order" | "Show me my last 20 orders" |
| Cares about | Being correct | Being fast |

If you use one model for both, you load a full `Order` object — with all its rules and child objects — just
to display a date and a total on a list screen. Twenty orders becomes hundreds of objects created and thrown
away.

**So we use two paths:**

- **Writes (commands)** go through the full `Order` object, with every rule enforced.
- **Reads (queries)** skip all of that and run a direct SQL query returning exactly the columns the screen
  needs.

**The most common misunderstanding:** CQRS does **not** mean two databases. It usually means two *code
paths* over the same database. (We do use a separate read database for Catalog — but only because product
browsing gets vastly more traffic than product editing. That is a performance decision, not a CQRS
requirement.)

📄 [ADR-0012](adr/0012-cqrs-with-mediatr.md)

---

## 16. Repository, Unit of Work, Mediator

Three patterns you will see named in the code.

### Repository — "pretend the database is a list"

Instead of writing SQL everywhere, your code says:

```csharp
var order = await _orders.GetByIdAsync(id);
```

The rest of the code has no idea whether that came from PostgreSQL, a file, or memory — which is what makes
it testable without a database.

**Rule: one repository per aggregate root.** There is an `OrderRepository`, never an `OrderItemRepository` —
that would be a way to sneak past the `Order` and break its rules.

### Unit of Work — "save it all, or none of it"

Make several changes, then commit them together. If anything fails, nothing is saved.

**In .NET you already have one:** Entity Framework's `DbContext`. You make changes, call `SaveChanges()`, and
it writes them as one transaction. A very common mistake is writing an `IUnitOfWork` wrapper around it —
that is an abstraction over an abstraction, adding nothing.

### Mediator — "a middleman so callers and handlers do not know each other"

Rather than a web endpoint calling a service class directly, it sends a message:

```csharp
await _mediator.Send(new PlaceOrderCommand(...));
```

Something else handles it. Why? Because you can then wrap **every** such message in shared behaviour —
validation, logging, transactions, duplicate detection — written once, applied automatically to all of them.
That is the real payoff, and it is what the "pipeline behaviours" in this codebase are.

---

# Security

## 17. OAuth2, OIDC, JWT and PKCE

Four intimidating names, one simple story.

### The problem

Our shop needs logins. Building that yourself means password hashing, brute-force protection, MFA, password
resets, session handling — forever, correctly, with no benefit to the business if you do it well and
catastrophe if you do it badly.

So we do not. **Keycloak** does it.

### How a login actually works

1. You click "Sign in" on the shop.
2. The shop **redirects you to Keycloak**. You leave our website.
3. You type your password **on Keycloak's page**. Our code never sees it.
4. Keycloak redirects you back with a **token**.
5. The shop sends that token with every request. Services check it.

### The four words

**OAuth2** — the rulebook for "how an app gets permission to act for a user, without seeing their password".

**OIDC** (OpenID Connect) — a thin layer on top of OAuth2 that adds *"and here is who the user is"*. OAuth2
alone handles permission; OIDC adds identity.

**JWT** (JSON Web Token) — the token itself. A blob of text with three parts: who you are, what you may do,
and a **signature**. The signature is the clever bit: it is made with Keycloak's private key, so any service
can verify the token is genuine **without calling Keycloak** — no network trip per request.

You can paste one into [jwt.io](https://jwt.io) and read it. **Note: a JWT is readable by anyone, just not
forgeable.** Never put secrets in one.

**PKCE** (pronounced "pixie") — protection for apps that cannot keep a secret.

> A normal server-side app proves its identity with a password ("client secret"). But a website's JavaScript
> and a phone app are downloaded onto the user's device — **anyone can read a secret inside them.** So they
> have none.
>
> PKCE fixes this: before starting, the app invents a random value, keeps it, and sends a *scrambled version*
> of it. At the end it must show the original. An attacker who steals the code midway cannot use it, because
> they do not have the original random value.

Our storefront, admin panels and mobile app all use PKCE and hold **no secret**.

📄 [ADR-0005](adr/0005-keycloak-as-identity-provider.md)

---

## 18. Roles vs permissions

**A role** is a job title: `customer`, `catalog-manager`, `admin`.

**A permission** is a specific ability: `catalog:write`, `order:refund`.

**The naive approach — check the job title:**

```csharp
[Authorize(Roles = "admin,order-manager")]
public void RefundOrder() { }
```

This works until the business says *"support agents should be able to issue refunds too."* Now you must find
**every** place refunds are checked and add `support-agent`. Miss one and it silently keeps working the old
way — a security bug that announces nothing.

**The better approach — check the ability:**

```csharp
[RequirePermission("order:refund")]
public void RefundOrder() { }
```

This code **never changes again.** Which roles hold `order:refund` is configuration in Keycloak. Granting it
to support agents is a settings change, with no deployment at all.

Keycloak links them with **composite roles** — "the `order-manager` role includes `order:refund` and
`order:cancel`" — so the token carries **abilities**, not job titles.

**One more thing, and it matters.** The admin UI hides buttons you are not allowed to use. **That is
convenience, not security.** Anyone can call the API directly with the same token. So every rule enforced in
the screen is *independently* enforced on the server. We have tests that call protected endpoints with a
low-privilege token and require rejection — proving the server does not rely on the button being hidden.

📄 [`docs/authorization-model.md`](authorization-model.md)

---

# Keeping it running

## 19. Retry, circuit breaker, timeout

Three different protections for three different failures. They are often confused.

### Timeout — "I will not wait forever"

Without one, a call to a hung service hangs your service too, and the failure spreads upward. Every network
call needs one.

### Retry — "try again, it might have been a blip"

Networks glitch. Trying again a moment later usually works.

**But do it properly:**
- **Backoff** — wait longer each time (1s, 2s, 4s), instead of hammering something already struggling.
- **Jitter** — add a random wobble. Without it, ten services that started together retry at the *exact same
  moments*, arriving as a synchronised stampede. This is the part people forget.

### Circuit breaker — "stop trying, it is properly down"

Retries help with a blip. If a service is genuinely **down**, retrying makes things *worse* — every request
waits, holding threads and connections, until your service falls over too.

**Like an electrical fuse.** After enough failures the breaker "trips" and calls fail *instantly* without
even trying. After a pause it lets one through to test the water; if that works, normal service resumes.

> **The distinction to remember:** a **retry** handles a *temporary blip*. A **circuit breaker** handles a
> *sustained outage* — by giving up fast, so one broken service does not drag down everything that calls it.

---

## 20. Health checks

Two URLs on every service that answer two *different* questions.

| URL | Question | If it fails |
|-----|----------|-------------|
| `/health/live` | Is this program broken beyond saving? | **Restart me** |
| `/health/ready` | Can I do useful work right now? | **Stop sending me traffic** (but leave me alone) |

**Why the difference matters enormously.** Suppose you put a database check in `/health/live`. The database
hiccups for 30 seconds. Every copy of your service fails its liveness check at once, so the system restarts
them all. **But restarting does not fix a database.** They come back and fail again — and each restart
reopens database connections, adding load to the database that was already struggling.

A 30-second blip has become a total outage that keeps itself alive.

**The rule: `/health/live` must never check anything a restart cannot fix.**

Try it:
```bash
curl http://localhost:5001/health/live
curl http://localhost:5001/health/ready
```

📄 [`docs/operations/health-checks.md`](operations/health-checks.md)

---

## 21. Logging, tracing, correlation IDs

With nine services, one customer click touches five of them. When something breaks, which one broke?

### Structured logging

Instead of writing a sentence:
```
Order 12345 failed for customer 987
```
we write data with named fields:
```csharp
_logger.LogError("Order {OrderId} failed for {CustomerId}", 12345, 987);
```

Now `OrderId` is a **searchable field**, not part of a string. We can ask "all failures for customer 987"
as a query. **Seq** (http://localhost:8081) is where you search them.

### Correlation ID

A unique ID attached to a request and passed along to every service it touches. Search that one ID and you
get **every log line from every service for that one customer action**, in order.

Try it:
```bash
curl -H "X-Correlation-Id: test-123" http://localhost:5001/
```
Then search `CorrelationId = 'test-123'` in Seq.

### Distributed tracing

Logs tell you *what happened*. Tracing tells you **where the time went**:

```
Place order                             1,240ms total
├─ create order          180ms
├─ reserve stock         240ms
├─ take payment          710ms   ← here is your problem
└─ confirm order          90ms
```

**Jaeger** (http://localhost:16686) draws these. You cannot get this picture from logs — you would be
subtracting timestamps across five machines by hand.

**Use both:** Jaeger shows you *which step* was slow, then you jump to Seq to read what that code actually
said.

📄 [tooling guide](operations/tooling-guide.md) · [observability](operations/observability.md)

---

## 22. Docker and Docker Compose

**The problem.** "It works on my machine" — because your machine has the right .NET version, the right
database, the right settings. A colleague's does not.

**A container** packages the program *together with everything it needs to run*. Like a shipping container:
the ship does not care what is inside, it just carries the box. The same container runs identically on your
laptop, a colleague's, and a server.

**Docker** builds and runs containers.

**Docker Compose** runs *many* containers together and wires them up. Our
[`deploy/docker-compose.yml`](../deploy/docker-compose.yml) describes 27 of them — nine services, nine
databases, a broker, a login server, and the debugging tools — and starts them all with:

```bash
docker compose up -d
```

**An image vs a container:** the *image* is the recipe; the *container* is the meal cooked from it. One
image, many containers.

**Two useful details from our setup:**

- **Health-check dependencies.** Compose waits until a database reports *healthy* before starting the service
  that needs it — rather than "sleep 30 seconds and hope", which is too long on a fast machine and too short
  on a busy one.
- **Two networks.** The databases sit on a network the browser cannot reach at all. Not just
  password-protected — **there is no route**. Only the BFFs bridge both sides.

📄 [`docs/getting-started.md`](getting-started.md) · [deployment topology](diagrams/deployment.md)

---

# Where to go next

| If you want to… | Read |
|-----------------|------|
| Use the four tools (Seq, Jaeger, RabbitMQ, Keycloak) | [operations/tooling-guide.md](operations/tooling-guide.md) |
| Understand what each service does in detail | [services/README.md](services/README.md) |
| See how the pieces fit together | [architecture.md](architecture.md) |
| Learn the business vocabulary | [domain/glossary.md](domain/glossary.md) |
| Know *why* each choice was made | [adr/README.md](adr/README.md) |
| Get the interview-ready version of this page | [concept-map.md](concept-map.md) |
