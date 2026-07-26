# Integration event catalogue

> **Related:** [ADR-0016 — RabbitMQ behind IEventBus](../adr/0016-rabbitmq-behind-ieventbus.md) ·
> [ADR-0010 — Transactional Outbox](../adr/0010-transactional-outbox.md)

Every integration event in the system: publisher, subscribers, payload, and failure behaviour.

**An integration event is a published contract.** Once another service consumes it, changing it is a breaking
change to someone else's deployment. This catalogue is where that contract lives, and an event that is not
here does not exist as far as other teams are concerned.

## Rules that apply to every event

| Rule | Why |
|------|-----|
| **Past tense, always** — `OrderConfirmed`, never `ConfirmOrder` | An event reports something that has happened and cannot be refused. A subscriber may react; it may not veto. |
| **Primitives and simple DTOs only** | Serialising a domain type exports your internal model as a public contract, so refactoring it breaks a neighbour. |
| **Additive changes only** | Add optional fields; never remove or repurpose one. Consumers tolerate unknown fields, so additive change needs no coordinated release — which is what makes independent deployability real. |
| **Delivery is at-least-once** | Every consumer must be idempotent. Not optional: a duplicate `OrderPaymentSucceeded` handled twice charges a customer twice. |
| **No ordering guarantee** | Concurrent outbox dispatchers can publish out of order. Where order matters, the consumer enforces it with a version number — it is never assumed from the transport. |
| **Published via the outbox** | Never directly from a handler. See [ADR-0010](../adr/0010-transactional-outbox.md). |

## Template

Each event is documented as:

```markdown
### <EventName>

- **Publisher:** service
- **Subscribers:** service (what it does) · service (what it does)
- **Routing key:** EventName
- **Version:** 1

**Payload**
| Field | Type | Notes |
|-------|------|-------|

**Idempotency:** how a duplicate is detected and what happens.
**Ordering:** whether the consumer tolerates out-of-order delivery.
**On failure:** retries, then dead-letter, and the business consequence of the message never being processed.
```

## Planned events

Documented here in full as each is implemented. Listed now so the intended flow is visible:

| Event | Publisher | Subscribers | Phase |
|-------|-----------|-------------|-------|
| `UserRegistered` | Keycloak (via back-office) | User Profile — provision a profile | 5 |
| `ProductPriceChanged` | Catalog | Basket — update and flag affected lines | 6 |
| `BasketCheckedOut` | Basket | Ordering — create the order | 6 |
| `OrderStarted` | Ordering | Ordering Saga — begin the process | 7 |
| `ReserveStock` (command) | Ordering Saga | Inventory | 7 |
| `StockReserved` / `StockRejected` | Inventory | Ordering Saga | 7 |
| `RequestPayment` (command) | Ordering Saga | Payment | 7 |
| `OrderPaymentSucceeded` / `OrderPaymentFailed` | Payment | Ordering Saga | 7 |
| `ReleaseStock` (compensation) | Ordering Saga | Inventory | 7 |
| `OrderConfirmed` | Ordering | Notification, Catalog | 7 |
| `OrderCancelled` | Ordering | Notification, Inventory | 7 |
| `StockLevelChanged` | Inventory | Catalog — update the cached display figure | 7 |

Note that the saga sends **commands** (imperative, one recipient, may be refused) as well as consuming
**events** (past tense, broadcast, cannot be refused). They travel over the same broker but are different
concepts, and conflating them is how an "event" quietly acquires exactly one required listener.

## The event flow, drawn

See [diagrams/event-flow.md](../diagrams/README.md) for the publisher/subscriber graph and the outbox
path — arriving with the first real events in Phase 6.
