# Runbook

Operational tasks, written as procedures rather than prose. Grows as the system gains behaviour worth
operating.

> Day-to-day development commands live in [getting-started.md](../getting-started.md). This page is for
> things that go wrong, or that are done rarely enough to be forgotten.

## Reset everything

```bash
cd deploy
docker compose down -v          # containers AND volumes - all data gone
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
the rule this architecture exists to prevent** - see
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
4. If the message: discard it and record why. **Never** re-queue a message that can never succeed - that is
   how one bad message saturates a consumer and blocks everything behind it.

## Requeue a parked outbox message

The outbox parks a message after 25 failed publishes (see
[ADR-0022](../adr/0022-edge-hardening-defaults.md)): the publisher's fetch skips it, and its final
attempt logged at ERROR. The row is still in the table with its `last_error`.

1. Find it - any service's database, same table shape everywhere:

   ```sql
   SELECT id, event_name, attempts, last_error
   FROM outbox_messages
   WHERE published_at IS NULL AND attempts >= 25;
   ```

2. Read `last_error` and fix the CAUSE first. Requeueing a message that can never publish just buys
   another 25 failures.

3. Requeue by resetting the counter:

   ```sql
   UPDATE outbox_messages SET attempts = 0
   WHERE published_at IS NULL AND attempts >= 25;
   ```

The publisher picks it up on its next pass. Nothing needs restarting.

## Demonstrate the stuck-saga sweep

The saga compensates itself when a step's answer never arrives. Dev compose sets the threshold to two
minutes so this can be watched (production default is fifteen):

1. `docker compose stop payment-api`
2. Place an order in either storefront. It sits at "Awaiting payment".
3. Within ~2.5 minutes the sweeper cancels it: status `Cancelled`, reason `TimedOut`, the timeline
   shows `TimedOut` then `CompensatingReleaseStock` - the stock went back.
4. `docker compose start payment-api`. The queued payment command is consumed LATE and succeeds -
   money taken for a cancelled order - and the saga answers with `RefundRequested`. The payments
   table shows `refunded_at` set. That closed loop is the whole point:

   ```sql
   -- docker compose exec payment-db psql -U ecom -d payment
   SELECT order_number, refunded_at IS NOT NULL AS refunded FROM payments
   ORDER BY created_at DESC LIMIT 3;
   ```

## A client is getting 429s

The BFFs budget 1000 requests per 10 seconds per client IP (configuration:
`RateLimiting__PermitLimit` / `RateLimiting__WindowSeconds`). The response carries `Retry-After`.
One legitimate client hitting this is a client-side loop bug; many clients hitting it from one IP is a
shared NAT, and the budget is the knob. Health endpoints are exempt, so probes never trip it.

## Reseed demo data

_Phase 4 onward._ `SEED_DEMO_DATA=true` in `.env` seeds on startup; seeding is idempotent, so a restart is
safe.

## Rotate a client secret

1. Regenerate it in the Keycloak admin console for that client.
2. Update the matching `*_CLIENT_SECRET` in `deploy/.env`.
3. `docker compose up -d <service>` - the service picks it up on restart.

Only confidential clients (BFFs, back-office, saga) have secrets. Public clients - the SPAs and the mobile
app - deliberately have none; they use PKCE ([ADR-0005](../adr/0005-keycloak-as-identity-provider.md)).

## Free disk space

```bash
docker system df                # what is using it
docker builder prune            # build cache only - safe, keeps images
docker system prune -af         # everything unused - frees a lot, next build is slow
```

## Apply a migration outside the container

```bash
dotnet ef database update \
  --project src/services/catalog/ECommerce.Catalog.Infrastructure \
  --startup-project src/services/catalog/ECommerce.Catalog.Api
```

There is no solution-wide migration step, and that is deliberate: each service owns its own schema and its own
migration history.
