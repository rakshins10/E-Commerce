# Health checks

> **Code:** [`HealthCheckExtensions.cs`](../../src/building-blocks/Observability/HealthCheckExtensions.cs) ·
> **Related:** [Deployment topology](../diagrams/deployment.md)

Every service exposes two endpoints. They answer different questions and have **opposite consequences**, and
conflating them is one of the more damaging mistakes in container operations.

| Endpoint | Question | Checks | Failing it means |
|----------|----------|--------|------------------|
| `/health/live` | Is this process irrecoverably broken? | Nothing external | **Kill and restart me** |
| `/health/ready` | Can I serve traffic right now? | Database, broker, cache | **Stop routing to me — but leave me alone** |

## Why the distinction is not academic

Put a database check in **liveness**, and this happens: the database has a thirty-second blip. Every replica
fails its liveness probe simultaneously. The orchestrator kills them all and starts fresh ones. Restarting a
service does not fix a database — so the new replicas fail too, and you now have a crash loop. Worse, every
restart reopens a connection pool, so the restarts add a connection storm to a database that was already
struggling.

**A recoverable dependency blip has become a full outage that outlives its cause.**

The rule that prevents it: **liveness must never check anything that a restart cannot fix.** A deadlocked
thread pool, an unrecoverable `OutOfMemoryException`, a corrupt in-process cache — those a restart fixes. A
database being down, it does not.

## Why the liveness check looks pointless

```csharp
services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: [LivenessTag]);
```

A check that always returns healthy appears to test nothing. It is not nothing: **reaching it at all** proves
the process is running, Kestrel is accepting connections, the request pipeline executes, and the thread pool
is not starved. That is precisely — and only — what liveness should assert.

## Readiness and the dependencies each service checks

Registered with the `ready` tag as each service gains them:

| Service | Readiness checks |
|---------|------------------|
| catalog | Postgres, MongoDB, RabbitMQ |
| basket | Redis, RabbitMQ |
| ordering, payment, inventory, notification, user-profile, saga | Postgres, RabbitMQ |
| back-office | Postgres (audit), Keycloak discovery endpoint |
| BFFs | Keycloak discovery endpoint |

Note what is **not** checked: a BFF does not check the services behind it. If Catalog is down, the storefront
BFF can still serve basket and order routes, and marking it unready would take down functionality that works.
**Readiness means "can I do my job", not "is everything downstream perfect"** — the alternative propagates a
single service's outage across the whole edge.

## The response body

The default writer returns only the aggregate status as plain text, which tells an operator *something* is
wrong but not what. Ours names each check:

```json
{
  "status": "Degraded",
  "totalDurationMs": 12.4,
  "checks": [
    { "name": "self",     "status": "Healthy",   "durationMs": 0.01 },
    { "name": "postgres", "status": "Healthy",   "durationMs": 4.2 },
    { "name": "rabbitmq", "status": "Unhealthy", "description": "Broker unreachable", "durationMs": 8.1 }
  ]
}
```

Exception detail is deliberately excluded. A health endpoint is often reachable from further away than
intended, and a stack trace or a connection string in the body is a genuine information leak.

## How Compose uses them

```yaml
healthcheck:
  test: ["CMD-SHELL", "curl -fsS http://localhost:8080/health/live || exit 1"]
  interval: 10s
  start_period: 20s

depends_on:
  catalog-db: { condition: service_healthy }
```

`start_period` matters: during it, failures do not count against the retry budget, so a service that legitimately
takes fifteen seconds to warm up is not killed for it.

`docker compose up --wait` blocks until everything is healthy and **fails if a service starts and then
crashes** — which a plain `up -d` reports as success. That is why CI uses it as the "the stack actually boots"
gate.

## Mapping to Kubernetes later

Kubernetes is not built here, but the split is already correct, so layering it on is configuration:

```yaml
livenessProbe:
  httpGet: { path: /health/live, port: 8080 }
  periodSeconds: 10
readinessProbe:
  httpGet: { path: /health/ready, port: 8080 }
  periodSeconds: 5
startupProbe:            # what Compose approximates with start_period
  httpGet: { path: /health/live, port: 8080 }
  failureThreshold: 30
  periodSeconds: 2
```

A `startupProbe` is the cleaner mechanism for slow starts: it suspends the other two until the app is up, so
liveness can use a short, aggressive period afterwards without risking a restart during warm-up.
