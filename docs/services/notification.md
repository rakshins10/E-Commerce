# Notification service

> **Bounded context:** Notification (supporting) · **Port:** 5006 · **Store:** PostgreSQL
> **Code:** [`src/services/notification/`](../../src/services/notification/)
> **Related:** [User Profile](user-profile.md) · [Ordering saga](ordering-saga.md)

## Purpose

Tells the customer when something happens to their order.

---

## The clearest example of why consumers must be idempotent

Sending an email is **not naturally idempotent**. There is no "send this unless you already did"
operation, and the second one is in the customer's inbox by the time you notice.

Every other service here can shrug off a duplicate — `MarkAsPaid` on a paid order returns quietly,
`Release` clamps at zero. This one cannot. So it is the one place that uses the **inbox** half of the
outbox pattern explicitly:

```csharp
db.Notifications.Add(new Notification(...));
db.ProcessedMessages.Add(new ProcessedMessage(messageId, eventName, "notification"));
await db.SaveChangesAsync();          // ONE transaction
```

**That single transaction is the entire trick.** Recording the message id separately would reintroduce
the dual-write problem the outbox exists to solve, one layer down: a crash between the two would either
send twice or record a send that never happened.

> **Prefer natural idempotency where it is achievable.** An operation that is idempotent by construction
> needs no bookkeeping and cannot get the bookkeeping wrong. This table is for the operations that are
> not — sending an email, incrementing a counter, calling a third party.

The `consumer` column is part of the composite key, because one service can legitimately have several
handlers for the same event. Deduplicating on the message id alone would let whichever handler ran first
silently suppress the others.

---

## No email is actually sent

Notifications are written to a table and logged. Wiring an SMTP server into a reference repo adds a
credential to manage and a thing to break, and changes nothing about the pattern being demonstrated.

In a real system this row would still exist — it is what lets support answer *"did we tell them?"* and
what a resend is built from. The SMTP call would be an additional step, not a replacement for it.

---

## The wording depends on *why*

```
PaymentDeclined     -> "Nothing has been charged. Please check your payment details and try again."
OutOfStock          -> "An item sold out before we could reserve it. Nothing has been charged."
RequestedByCustomer -> "Your order has been cancelled as you asked."
```

This is exactly why the cancellation reason is **part of the event** rather than something a consumer has
to go and ask for. Those three situations need completely different messages, and the second needs to
tell the customer what to do next.

An unrecognised reason falls through to a generic message rather than crashing — which is only possible
because the reason travels as a string. See
[ADR-0019](../adr/0019-shared-integration-event-contracts.md).

---

## Marketing consent is deliberately not consulted

These are **service** messages — part of performing the contract the customer entered by buying something
— not marketing.

Under UK GDPR/PECR they do not require opt-in, and a customer who unsubscribed from adverts still expects
a dispatch email. The [User Profile service](user-profile.md) holds the other half of that distinction,
and keeps marketing and order-update preferences in separate groups for the same reason.

---

## Events

Consumes `OrderSubmitted`, `OrderPaid`, `OrderShipped` and `OrderCancelled`. **Publishes nothing.**

A service that only consumes is a **sink**, which is a perfectly normal shape. It still implements
`IOutboxContext`, purely for the `processed_messages` table.

---

## Configuration

| Key | Default | Notes |
|-----|---------|-------|
| `ConnectionStrings__NotificationDb` | `Host=notification-db;…` | From `deploy/.env` |
| `EventBus__HostName` | `rabbitmq` | |

## Health

| Probe | Route | Checks |
|-------|-------|--------|
| Liveness | `/health/live` | Process is up |
| Readiness | `/health/ready` | Database reachable and migrated |
