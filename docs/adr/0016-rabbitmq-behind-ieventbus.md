# ADR-0016: RabbitMQ as the broker, behind an `IEventBus` abstraction

- **Status:** Accepted
- **Date:** 2026-07-25
- **Phase:** 1

## Context

Asynchronous integration events are the primary communication mechanism in this system
([architecture.md §6](../architecture.md#6-communication-choosing-synchronous-or-asynchronous)). That needs a
broker, and it needs to support:

- **Publish/subscribe** — one publisher, many independent subscribers, each with its own queue and its own
  failure handling.
- **Durability** — a message survives a broker restart.
- **Competing consumers** — several instances of a service share a queue for scale.
- **Dead-lettering** — a message that cannot be processed goes somewhere visible rather than being lost or
  looping forever.
- **Publisher confirms** — the outbox dispatcher must know a message was accepted before marking a row
  processed ([ADR-0010](0010-transactional-outbox.md)).
- **Running in a container** so `docker compose up` works offline.

## Options considered

### Option A — RabbitMQ
A mature AMQP 0-9-1 broker. Flexible exchange/binding routing, per-queue dead-letter exchanges, publisher
confirms, a good management UI, a small container, and the broker most .NET developers have used.

Its model is **smart broker, dumb consumer**: routing happens in the broker, messages are removed when
acknowledged, and there is no built-in replay.

### Option B — Apache Kafka
A distributed, partitioned, replayable log. **Dumb broker, smart consumer** — messages are retained by
policy, consumers track their own offset and can rewind.

Excellent when you need replay, very high throughput, event sourcing, or stream processing. Rejected here:
none of those requirements exist, and the operational weight is substantial (partitions, consumer groups,
rebalancing, retention tuning; historically ZooKeeper, now KRaft). Kafka's ordering guarantee is per
partition, which drags key-design into every producer. **For pub/sub with dead-lettering and no replay
requirement, Kafka is the wrong tool** — and being able to say *why* is a better answer than defaulting to
it.

### Option C — Azure Service Bus
Managed, excellent .NET SDK, sessions, scheduled delivery, and native dead-letter queues.

Rejected for one hard reason: it cannot run in a container. It would mean no offline development, an Azure
subscription to run the repository, and integration tests dependent on a shared cloud resource. It remains
the natural production target on Azure — which is precisely why the abstraction below exists.

### Option D — No broker; HTTP callbacks between services
Rejected. It reintroduces exactly the runtime coupling events exist to remove, and rebuilds retry,
durability, and fan-out by hand — badly.

## Decision

**RabbitMQ as the broker, accessed only through an `IEventBus` abstraction defined in
`building-blocks/EventBus`, with the RabbitMQ implementation isolated in `building-blocks/EventBus.RabbitMQ`.**

```csharp
public interface IEventBus
{
    Task PublishAsync(IntegrationEvent @event, CancellationToken ct = default);
    void Subscribe<TEvent, THandler>()
        where TEvent : IntegrationEvent
        where THandler : IIntegrationEventHandler<TEvent>;
}
```

### Why the abstraction is worth having here

"Abstract it so you could swap it" is usually a weak argument — most such abstractions are never exercised
and leak the underlying technology anyway. Three specific reasons make it worth it in this case:

1. **The swap is realistic, not hypothetical.** Deploying this to Azure would mean Azure Service Bus. The
   abstraction is aimed at a substitution that would actually happen.
2. **The abstraction is genuinely thin.** Publish and subscribe, over durable topic-routed pub/sub — the
   intersection of what every broker offers. It does not attempt to abstract exchange types, prefetch, or
   consumer groups, which is where such abstractions normally leak and fail.
3. **It keeps services ignorant of the transport.** No service references `RabbitMQ.Client`; only composition
   roots reference the implementation package. That is what keeps the dependency direction honest, and it is
   enforced by the architecture test from [ADR-0008](0008-monorepo.md).

**What the abstraction deliberately does not hide:** at-least-once delivery and the absence of ordering
guarantees. Those are properties of *distributed messaging*, not of RabbitMQ, and hiding them would produce
consumers that assume exactly-once and break on any broker. Consumers are idempotent because the semantics
require it ([ADR-0010](0010-transactional-outbox.md)) — not because of a RabbitMQ quirk.

### Topology

- **One topic exchange** (`ecommerce.events`), durable.
- **Routing key = event name** (`OrderStartedIntegrationEvent`).
- **One queue per service per subscribed event**, durable, so each subscriber gets its own copy and its own
  failure handling. A poison message in Notification must not affect Inventory.
- **Competing consumers** within a service: instances share a queue, so scaling out spreads load.
- **A dead-letter exchange per queue.** After a capped number of failed attempts a message is dead-lettered
  rather than retried forever — an infinite retry loop is a denial-of-service against your own system.
- **Publisher confirms** enabled, so the outbox dispatcher marks a row processed only after the broker has
  acknowledged it.

## Consequences

### What this buys us
- Temporal decoupling: a subscriber can be down and messages wait.
- Independent failure handling per subscriber.
- Horizontal scale via competing consumers with no code change.
- A management UI that makes queue depth and dead letters visible during a demo — worth a great deal when
  explaining the system to someone.
- Small container, fast startup, works offline.
- A realistic path to Azure Service Bus.

### What this costs us
- **A new operational dependency and a new failure mode.** The broker being down is now a thing that
  happens, and the outbox is what makes it survivable.
- **No replay.** Once acknowledged, a message is gone. If a consumer had a bug and processed a thousand
  messages wrongly, there is no rewind — recovery means a corrective process, not a replay. This is the
  single strongest argument for Kafka and it should be conceded plainly.
- **No global ordering.** Nothing may assume message sequence; ordering must be enforced by the consumer
  where it matters.
- **Queue and binding sprawl.** Queue-per-service-per-event grows quickly and needs naming discipline.
- **The abstraction will resist some optimisations** — priority queues, delayed delivery — which would either
  leak through or be forgone. Accepted; if one becomes necessary, the honest move is to widen the interface
  deliberately rather than smuggle the dependency in.

### What we will have to revisit
The trigger to move to Kafka is a *replay* or *stream-processing* requirement — for example an analytics
pipeline that needs to rebuild state from history, or event sourcing. Throughput alone is unlikely to be the
trigger; RabbitMQ handles far more than this system will ever produce.

## References

- [.NET microservices guide — implementing an event bus with RabbitMQ](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/multi-container-microservice-net-applications/rabbitmq-event-bus-development-test-environment)
- [ADR-0010](0010-transactional-outbox.md) — reliable publishing into this broker
- [events/event-catalogue.md](../events/event-catalogue.md) — the events themselves
