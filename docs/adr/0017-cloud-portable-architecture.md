# ADR-0017: Cloud-portable architecture — the same code runs on Docker or on Azure

- **Status:** Accepted
- **Date:** 2026-07-26
- **Phase:** 3 (seam established), providers implemented 4–12

## Context

The system must run in **two environments with no code change**:

1. **On-premises / local** — `docker compose up`, no cloud account, no internet dependency, no cost. This is
   how the repository is developed, demonstrated, and reviewed.
2. **Azure** — the same services running against managed platform services, to demonstrate cloud
   architecture and to be hostable somewhere with a free tier.

This is not merely "nice to have". It is a **senior/architect interview topic in its own right**: *"how do
you avoid cloud lock-in?"*, *"how would you migrate this to Azure?"*, *"what changes between environments?"*
An answer of "we'd rewrite the infrastructure layer" is a weak one.

There is a real trap here. The naive approach — an abstraction over every cloud service so nothing is
"locked in" — produces a lowest-common-denominator wrapper that is worse than either option and demonstrates
neither.

## Options considered

### Option A — Build for Docker only, port later if needed
Simplest, and honest about YAGNI. But "port later" to a distributed system with nine services means touching
every service under time pressure, and the portability seams are exactly the ones that are painful to
retrofit. It also forfeits the interview topic entirely.

### Option B — Abstract everything behind our own interfaces
Total portability in theory. In practice it means writing a wrapper over blob storage, over queues, over
secrets, over configuration, over identity — each one exposing the intersection of what every provider
offers, so you lose Service Bus sessions, lose Blob Storage tiers, lose Key Vault soft-delete. **You end up
maintaining a worse version of five products** and cannot use the good parts of any of them.

### Option C — Abstract only where the abstraction is thin, and swap by configuration
Draw the seam at points where the *concept* is genuinely provider-independent (publish a message; read a
secret; store a blob) and accept provider-specific configuration everywhere else.

## Decision

**Option C.** Portability is achieved by **three mechanisms in order of preference**, not by one blanket
abstraction:

### 1. Configuration, where the protocol is standard

Most "portability" needs no code at all, because the protocol is already open:

| Concern | Local | Azure | Code change |
|---------|-------|-------|:---:|
| Relational DB | Postgres container | Azure Database for PostgreSQL | **none** — a connection string |
| Cache | Redis container | Azure Cache for Redis | **none** — a connection string |
| Identity | Keycloak container | Keycloak on Container Apps, **or** Entra External ID | **none** — OIDC discovery URL |
| Telemetry | Jaeger + Seq | Application Insights | **none** — the OTLP endpoint |
| Document store | MongoDB container | Azure Cosmos DB (Mongo API) | **none** — a connection string |

**This is the single most important row in this ADR.** Choosing OIDC, OTLP, PostgreSQL and the Mongo wire
protocol was already a portability decision — open protocols are what make configuration-only migration
possible. Half the work was done by picking standards in Phases 1–2.

### 2. A thin abstraction, only where implementations genuinely differ

Two places where the wire protocol differs enough to need code:

**Messaging** — `IEventBus` already exists ([ADR-0016](0016-rabbitmq-behind-ieventbus.md)) with a RabbitMQ
implementation. An `EventBus.AzureServiceBus` package implements the same interface. Services reference only
the abstraction; the composition root picks. The interface is deliberately *thin* — publish and subscribe
over durable topic pub/sub, the intersection of what every broker offers — which is why it does not leak.

**Secrets and blobs** — `ISecretStore` and `IBlobStore`, with local (env/file-system) and Azure (Key Vault /
Blob Storage) implementations. Both are genuinely narrow interfaces: get a secret by name, put and get a blob
by key.

### 3. Deliberate non-abstraction, documented

Anything **not** on the lists above is used directly and would need real work to migrate. Naming these is
more useful than pretending they do not exist:

- Azure Container Apps' scaling rules and revision model
- Key Vault's soft-delete and purge protection semantics
- Service Bus sessions and duplicate detection (we do not use them; our idempotency is our own)
- Application Insights' Kusto queries and workbooks

### How an environment is selected

One setting, read at startup:

```jsonc
// appsettings.json — local default
"Platform": { "Provider": "OnPremises" }
```

```bash
# Azure
Platform__Provider=Azure
```

The composition root branches once, in one place, and the rest of the system never knows:

```csharp
if (builder.Configuration.IsAzure())
{
    builder.Services.AddAzureServiceBusEventBus(builder.Configuration, serviceName);
    builder.Services.AddKeyVaultSecrets(builder.Configuration);
}
else
{
    builder.Services.AddRabbitMqEventBus(builder.Configuration, serviceName);
    builder.Services.AddEnvironmentSecrets(builder.Configuration);
}
```

**An architecture test asserts that no service project references an Azure SDK package**, so the seam cannot
erode quietly — the same mechanism that keeps `RabbitMQ.Client` out of the services
([ADR-0008](0008-monorepo.md)).

### Managed identity is the goal on Azure

Where Azure is used, services authenticate with **workload identity**, not connection strings with embedded
keys. `DefaultAzureCredential` picks up the managed identity in Azure and a developer's `az login` locally,
so the same code works in both — and there is no bootstrap secret at all, which is the flaw in
"put the secret in a vault" ([ADR-0009](0009-secrets-management.md)).

## Consequences

### What this buys us
- `docker compose up` keeps working forever, with no cloud account and no cost. That is the default path and
  the one CI exercises.
- A credible answer to "how would you take this to Azure?", with the seam visible in the code.
- Local development stays fast — no cloud round trips, no shared environment.
- The choice is reversible: an environment variable, not a rewrite.

### What this costs us
- **Two implementations to maintain** for messaging, secrets and blobs, and the Azure path is exercised far
  less than the local one. Mitigated by contract tests that run the same suite against both.
- **The abstraction constrains us.** We cannot use Service Bus sessions or Rabbit's delayed-delivery plugin
  through `IEventBus`. If one becomes necessary the honest move is to widen the interface deliberately, not
  to smuggle the dependency in.
- **A real risk of "portable to nowhere".** An abstraction that is never exercised against the second
  provider is fiction. This is the failure mode to watch, and it is why the Azure path gets an integration
  test rather than only a README claim.
- **Documentation doubles** for anything environment-specific.

### What we will have to revisit
If the Azure path were ever the *primary* deployment, this inverts: you would use Service Bus, Key Vault and
Container Apps directly, and the abstraction would be dead weight. Portability is worth paying for while
*both* targets are real — and stops being worth it the moment one wins.

## References

- [ADR-0016](0016-rabbitmq-behind-ieventbus.md) — the messaging abstraction this extends
- [ADR-0009](0009-secrets-management.md) — secrets, and why managed identity is the goal
- [`docs/operations/deployment.md`](../operations/deployment.md) — how each environment is deployed
- [`docs/operations/azure.md`](../operations/azure.md) — the Azure topology and its free-tier options
