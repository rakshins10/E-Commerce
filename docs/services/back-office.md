# Back-office service

> **Bounded context:** Back office (supporting) · **Port:** 5008 · **Store:** PostgreSQL
> **Code:** [`src/services/back-office/`](../../src/services/back-office/)
> **Related:** [Admin BFF](admin-bff.md) · [Authorization model](../authorization-model.md)

## Purpose

Three things the admin panel needs and no other service owns: **dashboard figures**, **user
administration**, and the **audit log**.

---

## 1. It reads other services' databases, and that needs justifying

This is the one place in the repo that crosses a data boundary. It is a deliberate, bounded exception
rather than an oversight, and the honest argument is worth stating in full.

**The alternative was rejected.** Calling each service over HTTP and aggregating means a dashboard fans
out to five services on every page load — and then the dashboard is down whenever any one of them is.
Reporting is the classic case where the boundary that helps write paths hurts read paths.

**What keeps it honest:**

| Rule | Why |
|------|-----|
| **Read-only** | Back-office never writes to another service's tables. |
| **Aggregates only** | `COUNT` and `SUM`. It never reads a row it would then act on — that would be reaching into somebody else's aggregate, and the invariants live there for a reason. |
| **Keyed connections** | `[FromKeyedServices("ordering")]`, so a query cannot reach the wrong database by accident. An unkeyed `IDbConnection` with five registrations resolves to whichever was registered last, and inventory figures read out of the ordering database produce *plausible nonsense* rather than an error. |

**What a production system would do instead:** publish figures as events into a reporting store, or use
a read replica per service. Both are more work and neither changes the shape of the code here. The
honest position is that this is a reference implementation choosing the simplest thing that demonstrates
the pattern, and saying so.

> The compose file uses one superuser, so "read-only" is documented rather than enforced. In a real
> deployment these connection strings would use accounts with `SELECT` granted and nothing else, so the
> database enforces the rule instead of a comment.

---

## 2. Users stay in Keycloak

This service keeps **no copy of users**. It calls the Keycloak Admin API and passes the answers through.

A local mirror would need syncing, would drift, and would produce the situation where the admin panel
says an account is enabled and the login page disagrees.

**What that costs:** the admin panel cannot show users when Keycloak is down. Since nobody can sign in
either, that is not much of a loss.

### The service account is narrow on purpose

| Granted | Not granted |
|---------|-------------|
| `view-users`, `query-users` | `realm-admin` |
| `manage-users` | `manage-realm` |
| `view-realm` (needed to *read* a role before mapping it) | anything client-scoped |

If this service is compromised, the blast radius should be *"can disable accounts in one realm"*, not
*"owns the identity provider"*.

`view-realm` was added after testing: `manage-users` alone permits changing a user but **not looking up
what a role is**, which surfaced as a 403 on role assignment that named nothing useful.

### Client credentials, not the caller's token

The signed-in manager has a token for `ecommerce-api`, which Keycloak's own admin endpoints do not
accept. So this service authenticates as itself.

**That ordering matters.** The caller's permission has already been checked, at the route, before this is
reached. The service account is powerful; the route is what decides who gets to use it. Calling this
before checking the permission would hand every signed-in user the service account's privileges.

The token is cached and refreshed **30 seconds early** — the classic off-by-one that otherwise produces a
401 on one request in a thousand and is miserable to reproduce.

### Two guards worth noting

**Role assignment is checked against an allow-list.** Without it, a request naming a Keycloak built-in
such as `realm-admin` would be privilege escalation delivered by JSON.

**You cannot disable your own account.** An administrator who does is locked out of the tool that could
undo it, and somebody has to go into Keycloak directly. Cheap to prevent, tedious to recover from.

---

## 3. The audit log

**Append-only, and that is the entire security property.** There is no update and no delete — not as an
oversight, but because an audit log somebody can edit is not evidence of anything. If the code cannot
modify a row, a compromised service cannot cover its tracks.

`AuditWriter` has exactly one method and no way to read or modify. Narrow on purpose.

### Why a separate log when every service already logs

| Application logs | Audit log |
|---|---|
| "What did the system do" | "Who did this, and were they allowed to" |
| Sampled, rotated | Kept |
| For engineers | For compliance, and for the conversation after an incident |

**The distinction that matters most: an audit entry records a *human decision*.** An order moving from
Paid to Shipped because the saga said so is not audited; a manager cancelling somebody's order is.

The actor's **username is copied**, not resolved at read time — for the same reason an order copies its
address. The log must still read correctly when somebody changes their username or leaves.

---

## 4. Endpoints

| Method | Route | Permission |
|--------|-------|-----------|
| `GET` | `/api/admin/dashboard` | `dashboard:read` |
| `GET` | `/api/admin/audit` | `audit:read` |
| `GET` | `/api/admin/users` | `user:read` |
| `GET` | `/api/admin/users/{id}` | `user:read` |
| `POST` | `/api/admin/users/{id}/enable` | `user:manage` |
| `POST` | `/api/admin/users/{id}/disable` | `user:manage` |
| `POST` | `/api/admin/users/{id}/roles` | `user:roles:manage` |
| `DELETE` | `/api/admin/users/{id}/roles/{role}` | `user:roles:manage` |

**Three tiers, deliberately.** Seeing who exists, changing whether they can log in, and granting them
power are different capabilities. `support-agent` holds only the first. Somebody who can assign roles can
grant themselves anything, which is why that is the narrowest permission in the whole model.

The audit limit is **clamped to 200**. An unbounded limit on an append-only table that only grows is a
denial of service delivered by query string.

---

## 5. A bug worth recording

`AuditEntryDto` was the only **positional record** among the read DTOs, and it failed at runtime with:

```
A parameterless default constructor or one matching signature … could not be found
```

The cause is two behaviours interacting:

1. **PostgreSQL folds an unquoted alias to lowercase**, so `AS ActorName` arrives as `actorname`.
2. **Dapper matches properties case-insensitively and constructor parameters case-sensitively.**

Every other read DTO in the repo already used `{ get; init; }` properties, which is why this was the only
place it bit. The error message names the constructor rather than the alias, which is exactly the sort of
misdirection that costs an hour.

---

## 6. Configuration

| Key | Notes |
|-----|-------|
| `ConnectionStrings__BackOfficeDb` | Its own database. Holds only `audit_entries`. |
| `ConnectionStrings__OrderingReadDb` | Read-only, aggregates only |
| `ConnectionStrings__SagaReadDb` | Read-only, aggregates only |
| `ConnectionStrings__InventoryReadDb` | Read-only, aggregates only |
| `KeycloakAdmin__ClientId` | `back-office-service` — a confidential client with no interactive login |
| `KeycloakAdmin__ClientSecret` | From `deploy/.env`; never committed |

## 7. Health

| Probe | Route | Checks |
|-------|-------|--------|
| Liveness | `/health/live` | Process is up |
| Readiness | `/health/ready` | Its **own** database only |

Readiness deliberately does not check the databases it reads from or Keycloak. A dashboard that cannot
show a stock figure should degrade, not take the service out of the load balancer.
