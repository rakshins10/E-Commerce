# Admin BFF

> **Kind:** Gateway (Backend-for-Frontend) · **Port:** 6002 · **Store:** none
> **Code:** [`src/gateways/admin-bff/`](../../src/gateways/admin-bff/)
> **Related:** [Storefront BFF](storefront-bff.md) · [Back-office](back-office.md) ·
> [ADR-0006 — BFF per client experience](../adr/0006-bff-per-client.md)

## Why a second gateway at all

The routing is not very different from the storefront's. The reason for a separate one is not technical
complexity — it is **who can reach it**.

The storefront BFF is reachable by every customer on the internet. This one exposes stock adjustments,
refunds and user administration. **One gateway carrying both means a mistake in a shared route table
hands admin surface to the public**, and route tables are exactly the kind of file where a catch-all gets
added at 5pm on a Friday.

Two gateways means the blast radius of that mistake is bounded by which door it was made on.

The same argument applies one level up: the admin apps have **their own Keycloak clients**
(`admin-react`, `admin-angular`) with their own redirect-URI allow-lists, so a redirect
misconfiguration on the shop cannot yield a token this gateway would accept.

---

## It is stricter than the storefront's

| | Storefront BFF | Admin BFF |
|---|---|---|
| Asks | *"Are you signed in?"* | *"Do you hold this permission?"* |
| Leaves authorization to | each service | each service, **and** checks it here |

The storefront gateway deliberately only requires authentication and lets each service decide the rest.
This one requires the permission **at the edge as well**, because the blast radius of a mistake is
larger.

**This is defence in depth, not a replacement.** Every service still checks independently — anything that
reached them by another path (another gateway, a misconfigured network policy, a developer with `curl`)
would otherwise be unauthorized.

---

## Routes

| Route | Methods | Permission | To |
|-------|---------|-----------|-----|
| `/api/catalog/{**}` | **GET only** | `catalog:read` | Catalog |
| `/api/orders/{**}` | **GET only** | `order:read` | Ordering |
| `/api/orders/{**}` | POST, PUT, DELETE | `order:cancel` | Ordering |
| `/api/inventory/{**}` | **GET only** | `inventory:read` | Inventory |
| `/api/inventory/{**}` | POST | `inventory:adjust` | Inventory |
| `/api/saga/{**}` | **GET only** | `order:read` | Ordering saga |
| `/api/admin/{**}` | all | `user:read` | Back-office |

### Reads and writes are separate routes on purpose

```jsonc
"orders-read":  { "Methods": ["GET"],                  "AuthorizationPolicy": "order:read" },
"orders-write": { "Methods": ["POST","PUT","DELETE"],  "AuthorizationPolicy": "order:cancel" }
```

A single catch-all would let anyone holding `order:read` issue a POST. Splitting them puts the **verb and
the permission next to each other**, so the route table can be audited by reading it.

The catalogue route is GET-only for the same reason the storefront's is: **catalogue writes arrive in
Phase 9 and will need their own route**, rather than being let in by a catch-all nobody revisits.

`/api/admin` requires only `user:read` here because Back-office checks a finer permission per endpoint —
`dashboard:read`, `audit:read`, `user:manage`, `user:roles:manage`. The gateway sets the floor for
reaching the service at all.

---

## Verified

Run against the live stack, four roles against five routes:

```
                dashboard  orders  inventory  users  audit
  administrator    200       200      200      200    200
  support          403       200      200      200    403
  ordermgr         403       200      200      403    403
  customer         403       403      403      403    403
```

**Same application, same build, three different navigation bars** — and a customer locked out entirely.
That is the payoff of guarding on permissions rather than roles: adding a permission to a composite in
Keycloak changes what people see with **no deployment**.

---

## CORS

```jsonc
"Cors": { "Origins": ["http://localhost:3001", "http://localhost:4201"] }
```

The **admin apps only**, never the storefront origins. An explicit allow-list rather than
`AllowAnyOrigin()`: with credentials in play, reflecting any origin lets any page on the internet make
authenticated admin calls on a signed-in manager's behalf.

---

## What it deliberately does not do

| Not here | Why |
|----------|-----|
| Business rules | A rule in the gateway is a rule the service can be bypassed to break |
| Its own database | A stateless gateway scales horizontally and has nothing to back up |
| Token exchange | The original Keycloak token is forwarded untouched, so every service sees the real caller and audit logs are true |
| Serving the SPAs | nginx does that, in the web image |

## Health

| Probe | Route | Checks |
|-------|-------|--------|
| Liveness | `/health/live` | Process is up |
| Readiness | `/health/ready` | Self only — **not** downstream services |

Readiness deliberately does not aggregate downstream health. One sick service marking the gateway
not-ready would take the *entire* back office dark over a single failing dependency. Per-cluster health
checks handle downstream state.
