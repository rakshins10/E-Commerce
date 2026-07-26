# Payment service

> **Bounded context:** Payment (supporting) · **Port:** 5004 · **Store:** PostgreSQL
> **Code:** [`src/services/payment/`](../../src/services/payment/)
> **Related:** [Ordering saga](ordering-saga.md)

## Purpose

Takes money for an order, and says whether it worked.

---

## This is a simulator, and it says so

There is no card number, no PCI scope and no provider. What it **does** model faithfully is the part
that matters architecturally:

- an operation that takes time;
- that sometimes fails;
- that must **never** be performed twice for the same order;
- whose failure triggers compensation elsewhere.

A real integration would replace `RequestPaymentHandler.AuthoriseAsync` and **change nothing else**. The
saga, the outbox and the compensation path are all unaffected by where the money comes from. That
separation is the point of putting payment behind an event boundary.

---

## The decline rule is deterministic, not random

```csharp
public const decimal DeclineThreshold = 5_000m;
```

**A threshold, not a random failure rate.** Random failures make a demo look realistic and make the test
suite flaky — the compensation path would pass or fail on the roll of a die, which is the fastest way to
get a suite ignored.

A threshold means the failure path is reachable **on demand**: put the £5,200 Leather Portfolio in a
basket and watch the saga release the stock and cancel the order. The seed data contains that product
specifically so this works from the UI.

The threshold is published on the service's identity endpoint (`GET /`), because it is the one piece of
configuration somebody demonstrating the compensation path needs to know, and hunting for it in a
constant is a poor use of anybody's time.

The 300 ms delay in `AuthoriseAsync` is not decoration either: it makes the asynchronous nature of the
saga visible. The order sits in `AwaitingPayment` for a moment, which is exactly what happens in
production and exactly what a UI must render without looking broken.

---

## Idempotency here protects real money

```csharp
if (existing is not null) return;   // already charged, or already declined
```

Delivery is at-least-once, so `RequestPaymentCommand` **will** arrive twice eventually. **Charging a
customer twice is the single worst bug this system could have.**

Two layers guard it:

1. the check above, and
2. a **unique index on `order_id`**, which is what saves you when two replicas handle duplicate
   deliveries concurrently and both pass the in-code check.

Note that a duplicate deliberately does **not** re-publish the outcome. The saga is idempotent too, but
two services each relying on the other to catch duplicates is how a duplicate gets through.

---

## Declines are recorded, not just successes

*"Why was my card refused?"* is the most common question this service will ever be asked, and a payment
service that keeps no record of its declines cannot answer it. It is also the evidence for a chargeback
dispute.

A `reference` is generated even for a decline, so support has something to quote.

---

## Refund is written but never sent

`RefundPaymentCommand` and its handler exist and are not reached by the current flow, because payment is
the last step that can fail.

They are written anyway, because **the shape of a saga is what makes adding a step afterwards safe**. If
a shipping-label step were added tomorrow, this is what would undo the charge — and having it now means
the compensation story is complete rather than aspirational.

It refuses to refund a declined or already-refunded payment, which would create money. Silently, not as
an error: a retried compensation reaching an already-refunded payment is the expected case, not an
exceptional one.

---

## Events

| Consumes | Publishes |
|----------|-----------|
| `RequestPaymentCommand` | `PaymentSucceededIntegrationEvent` |
| `RefundPaymentCommand` | `PaymentFailedIntegrationEvent` |
| | `PaymentRefundedIntegrationEvent` |

`PaymentFailed.Reason` is a **string, not an enum**, so a new decline reason does not break a consumer
that has not been redeployed. An unknown string falls into a default branch; an unknown enum value is a
deserialisation failure. See [ADR-0019](../adr/0019-shared-integration-event-contracts.md).

---

## Configuration

| Key | Default | Notes |
|-----|---------|-------|
| `ConnectionStrings__PaymentDb` | `Host=payment-db;…` | From `deploy/.env` |
| `EventBus__HostName` | `rabbitmq` | |
| `Outbox__PollingIntervalMs` | `1000` | |

## Health

| Probe | Route | Checks |
|-------|-------|--------|
| Liveness | `/health/live` | Process is up |
| Readiness | `/health/ready` | Database reachable and migrated |
