# ADR-0019 — Integration event contracts live in one shared project

**Status:** Accepted · **Date:** 2026-07-26 · **Phase:** 6

## Context

Ordering publishes `OrderSubmitted`, `OrderPaid` and four other integration events. Inventory, Payment,
Notification and the saga will all consume them from Phase 7.

Every consumer needs the shape of those messages. There are three ways to arrange that, and the choice
matters because it decides whether services stay independently deployable.

### Option A — each service hand-copies the record it consumes

The purist answer, and the one most microservice writing recommends. No shared code at all: Inventory
writes its own `OrderSubmitted` class with the fields it cares about.

**What it gets right:** total decoupling. Ordering can rename anything internally; Inventory only breaks
if the *wire format* changes, which is the honest boundary.

**What goes wrong in practice:** adding a field to `OrderPaid` is a silent mismatch. The producer sends
it, the consumer ignores it, and nobody finds out until someone asks why a report is empty. Removing one
is worse — the consumer deserialises a null into a non-nullable field and fails at 3am. There is no
compile step that can tell you the two have drifted, because by construction they are unrelated types.

### Option B — a shared library containing the domain model

The version people fall into. Ordering exposes its `Order` class, consumers reference it.

This is the failure mode this ADR exists to prevent. The moment a domain type crosses the boundary,
renaming a property inside the aggregate becomes a breaking change for three other teams, and the services
are no longer independently deployable. They are one distributed monolith with extra network calls.

### Option C — a shared library containing only the contract

One project holding integration event **records**: primitive properties, no behaviour, no domain types.

## Decision

**Option C.** [`src/contracts/ECommerce.Contracts`](../../src/contracts/ECommerce.Contracts/) is the one
thing services share.

The rule on what may live there is strict, and it is written into the `.csproj` so anyone adding a file
reads it first:

- records with **primitive** and primitive-collection properties, nothing else
- **no** behaviour, **no** domain types, **no** validation, **no** reference to any service
- **additive changes only** — a removed or renamed property breaks every consumer that has not redeployed

## Consequences

### The translation seam is what makes this safe

The aggregate raises a *domain* event carrying `Money` and `OrderStatus`. The application layer translates
it into an *integration* event carrying a `decimal` and a `string`:

```csharp
outbox.Add(new OrderPaidIntegrationEvent
{
    OrderId = order.Id,
    Total = order.Total.Amount,        // Money -> decimal
    Currency = order.Total.Currency,   // Money -> string
});
```

Those few lines are the whole point. They are what lets `Money` be refactored, split, or replaced without
any consumer noticing. **Skipping the translation and publishing the domain event directly is how Option C
silently becomes Option B.**

### Versioning is additive, because deploys are rolling

During a deployment the old and new versions run simultaneously and each sees the other's messages. New
properties must therefore be optional. When a genuinely incompatible change is needed, publish
`OrderPaidV2` alongside the original and retire it once consumers have moved.

### The enum-to-string rule

`OrderCancelledIntegrationEvent.Reason` is a `string`, not the domain enum. Adding a new cancellation
reason must not break a consumer that has not been redeployed — an unknown string falls into a default
branch, whereas an unknown enum value is a deserialisation failure.

### What this costs

A change to a contract recompiles every service that references it. That is real coupling, and it is
accepted deliberately: it converts a runtime mismatch into a compile error, which is the trade this whole
repo makes repeatedly. The strict content rule is what keeps that coupling to the wire format rather than
to the model behind it.

### The failure to watch for

If someone adds a helper method, a validation attribute, or a reference to `ECommerce.Ordering.Domain` to
this project, the boundary is gone and nobody will notice for months. That is why the reasoning sits in the
`.csproj` comment rather than only here — the warning belongs where the mistake would be made.

## Related

- [Ordering service](../services/ordering.md#8-integration-events)
- [ADR-0012 — CQRS](0012-cqrs-with-mediatr.md)
