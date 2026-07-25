# ADR-0010: Transactional Outbox for reliable event publishing

- **Status:** Accepted
- **Date:** 2026-07-25
- **Phase:** 1 (implemented in Phase 6)

## Context

When Ordering accepts an order it must do two things: **persist the order** to its own database, and
**publish `OrderStarted`** so the saga can begin. These are writes to two different systems — Postgres and
RabbitMQ — and there is no transaction spanning both.

That is the **dual-write problem**, and it has no correct naive solution:

```
BEGIN TX; save order; COMMIT;        // then the process crashes
bus.Publish(OrderStarted);           // never happens
```
→ The order exists, nothing is fulfilling it. It sits in `Submitted` forever. **Silent, permanent data loss
in the business sense** — the worst possible outcome, because nothing alerts.

Reversing the order is not better:

```
bus.Publish(OrderStarted);           // succeeds
BEGIN TX; save order; COMMIT;        // fails
```
→ The saga is now reserving stock and charging a card for an order that does not exist.

Wrapping the publish inside the database transaction only narrows the window; it does not close it, because
the broker call can succeed while the commit that follows fails.

## Options considered

### Option A — Publish inside the transaction and hope
Cheap; usually works. The failure mode is rare, silent, and corrupting — which is the worst combination,
because it will not show up in testing and will not announce itself in production.

### Option B — Two-phase commit (XA) across Postgres and RabbitMQ
Actually correct in theory. Rejected in practice: RabbitMQ has no usable XA support, distributed transactions
hold locks across a network round trip (destroying throughput), and a coordinator failure leaves in-doubt
transactions that block. 2PC is broadly abandoned in distributed systems for exactly these reasons, and
saying *why* is a better interview answer than not knowing it exists.

### Option C — Transactional Outbox
Write the event **into the same database, in the same transaction** as the business data. A separate
dispatcher polls the outbox table and publishes to the broker, marking rows dispatched.

The insight: there is now only **one** transactional resource. Either both the order and its
intent-to-publish commit, or neither does.

### Option D — Change Data Capture (Debezium reading the WAL)
The dispatcher reads the database's replication log instead of polling, so there is no polling latency and
no application-side dispatcher.

Genuinely superior at scale and worth naming. Rejected here: it adds Kafka Connect or equivalent to the
stack, moves a critical piece of behaviour outside the application where it is harder to test and reason
about, and couples the event pipeline to Postgres internals. For this system the operational weight is not
justified — but it is the right answer for very high throughput.

## Decision

**Transactional Outbox**, with a polling dispatcher.

### Shape

Each publishing service has an `outbox_messages` table in **its own** database:

| Column | Purpose |
|--------|---------|
| `id` | Primary key; **becomes the event's message id** — this is what makes consumer-side deduplication possible |
| `event_type` | Fully-qualified type name, for deserialisation and routing |
| `payload` | Serialised event (`jsonb`) |
| `occurred_at` | When the business fact happened |
| `processed_at` | Null until dispatched |
| `attempts`, `last_error` | For retry and poison-message diagnosis |
| `trace_id`, `correlation_id` | So the trace survives the async hop — without this, the distributed trace breaks at every event boundary |

The write path: the command handler mutates the aggregate, the domain events it raised are translated to
integration events and inserted into the outbox, and **`SaveChangesAsync` commits both atomically** —
the `DbContext` already being the Unit of Work
([architecture.md §4](../architecture.md#4-c4-level-3--anatomy-of-a-service)).

The dispatcher is a `BackgroundService` polling with `FOR UPDATE SKIP LOCKED`, which is the whole reason
[ADR-0003](0003-postgresql-and-polyglot-persistence.md) cares about Postgres: multiple instances can poll
the same table concurrently, each claiming a disjoint batch, with no coordination and no lock contention.
Publishing uses **publisher confirms** — a broker acknowledgement, not a local write — before a row is
marked processed.

### At-least-once, and why the consumer side is not optional

If the dispatcher publishes and then crashes before marking the row, the event is published **again**. The
outbox therefore guarantees *at-least-once*, never exactly-once.

**Exactly-once delivery is not achievable** across a network — the classic result. What is achievable is
*effectively-once processing*: at-least-once delivery plus **idempotent consumers**. Every consumer records
processed message ids in a `processed_messages` table in its own database, inside the same transaction as
its business work, and ignores duplicates.

**The outbox and the idempotent consumer are two halves of one pattern.** Implementing only the outbox moves
the bug from the publisher to the consumer — where it manifests as a customer charged twice. This is the
single most important thing to be able to say about this ADR.

## Consequences

### What this buys us
- A committed business fact **always** eventually reaches the broker. No silent loss.
- No distributed transaction and no coordinator.
- Events survive a broker outage — they queue in the table and drain on recovery.
- The outbox is a readable audit trail of what a service intended to publish and when.
- `trace_id` on the row keeps distributed tracing intact across the asynchronous boundary.

### What this costs us
- **Latency.** A poll interval (~1 s here) sits between commit and publish. Fine for these flows; it is
  exactly the cost CDC removes.
- **Duplicates are now normal**, so every consumer must be idempotent. Non-negotiable, and it is real work
  in every consumer.
- **Ordering is not guaranteed.** Concurrent dispatchers with `SKIP LOCKED` can publish out of order.
  Consumers must not assume sequence. Where order genuinely matters, it must be enforced by the consumer
  (version numbers on the aggregate), not assumed from the transport.
- **A table that grows forever** without a retention job archiving or deleting dispatched rows.
- **A poison message can stall a partition** if a row fails repeatedly. Needs an attempt cap and a
  dead-letter path, or one bad event blocks the queue behind it.
- **More moving parts per service**: a table, a background service, a metric, and a health check.

### What we will have to revisit
If publish latency or outbox table volume becomes a problem, move to CDC (Option D) — the event contracts
and consumer idempotency are unchanged, only the dispatcher is replaced. That is a deliberate property of
this design: the outbox table is the interface, not the polling.

## References

- [.NET microservices guide — reliable event publishing](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/multi-container-microservice-net-applications/subscribe-events#designing-atomicity-and-resiliency-when-publishing-to-the-event-bus)
- [microservices.io — Transactional Outbox](https://microservices.io/patterns/data/transactional-outbox.html)
- [ADR-0011](0011-orchestration-saga.md) — the saga that depends on reliable delivery
- [events/event-catalogue.md](../events/event-catalogue.md) — per-event idempotency expectations
