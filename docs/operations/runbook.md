# Runbook

Operational tasks, written as procedures rather than prose. Grows as the system gains behaviour worth
operating.

> Day-to-day development commands live in [getting-started.md](../getting-started.md). This page is for
> things that go wrong, or that are done rarely enough to be forgotten.

## Reset everything

```bash
cd deploy
docker compose down -v          # containers AND volumes — all data gone
docker compose up -d --wait
```

## Rebuild one service after a code change

```bash
docker compose up -d --build catalog-api
```

Only that image rebuilds; the restore layer is cached, so it is seconds rather than minutes.

## Find out why a service is unhealthy

```bash
docker compose ps                              # what is not healthy
docker compose logs --tail 100 catalog-api     # why
curl -s http://localhost:5001/health/ready | jq  # which check failed, by name
```

The readiness body names each check and its status, so this identifies the failing dependency without
guesswork. See [health-checks.md](health-checks.md).

## Inspect a service's database

Each service's Postgres is published on its own host port, so any client works:

```bash
psql -h localhost -p 15433 -U ecom -d catalog     # catalog
psql -h localhost -p 15434 -U ecom -d ordering    # ordering
```

Full port list: [`deploy/.env.example`](../../deploy/.env.example).

**Reading another service's database to answer a question is fine. Writing to it, or wiring code to it, is
the rule this architecture exists to prevent** — see
[data sovereignty](../architecture.md#7-data-sovereignty-why-services-never-share-a-database).

## Watch the event bus

RabbitMQ management UI: http://localhost:15672 (`ecom` / `dev_only_rabbit_pw`).

Worth knowing where to look:

- **Queues** → depth per queue. A growing queue means a consumer is down or too slow.
- **Queues ending `.dlq`** → dead-lettered messages. Anything here is a message that failed its full retry
  budget and needs a human.
- **Exchanges → `ecommerce.events`** → bindings, which shows who subscribes to what.

## Handle a poisoned message

_Detailed in Phase 7, once there are real consumers._ The shape:

1. Find it in the `.dlq` queue and read the payload and the `x-death` header for the failure count.
2. Decide whether the bug is in the message or the handler.
3. If the handler: fix, deploy, then shovel the message back to the main queue from the management UI.
4. If the message: discard it and record why. **Never** re-queue a message that can never succeed — that is
   how one bad message saturates a consumer and blocks everything behind it.

## Reseed demo data

_Phase 4 onward._ `SEED_DEMO_DATA=true` in `.env` seeds on startup; seeding is idempotent, so a restart is
safe.

## Rotate a client secret

1. Regenerate it in the Keycloak admin console for that client.
2. Update the matching `*_CLIENT_SECRET` in `deploy/.env`.
3. `docker compose up -d <service>` — the service picks it up on restart.

Only confidential clients (BFFs, back-office, saga) have secrets. Public clients — the SPAs and the mobile
app — deliberately have none; they use PKCE ([ADR-0005](../adr/0005-keycloak-as-identity-provider.md)).

## Free disk space

```bash
docker system df                # what is using it
docker builder prune            # build cache only — safe, keeps images
docker system prune -af         # everything unused — frees a lot, next build is slow
```

## Apply a migration outside the container

```bash
dotnet ef database update \
  --project src/services/catalog/ECommerce.Catalog.Infrastructure \
  --startup-project src/services/catalog/ECommerce.Catalog.Api
```

There is no solution-wide migration step, and that is deliberate: each service owns its own schema and its own
migration history.
