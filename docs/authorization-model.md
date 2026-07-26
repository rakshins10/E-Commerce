# Authorization model

> **Arrives in full in Phase 2**, alongside the Keycloak realm it describes.
> **Related:** [ADR-0005 — Keycloak as IdP](adr/0005-keycloak-as-identity-provider.md) ·
> [`src/building-blocks/Auth/`](../src/building-blocks/Auth/)

The complete role and permission matrix: every realm role, every permission, which composite grants what, and
which endpoint **and** which UI action each one guards.

## The design, stated now

### Two layers, deliberately

| Layer | Example | Answers |
|-------|---------|---------|
| **Realm role** — coarse identity | `customer`, `support-agent`, `catalog-manager`, `order-manager`, `admin` | *What kind of user is this?* |
| **Permission** — fine-grained capability | `catalog:write`, `order:refund`, `user:manage`, `price:override` | *What may they do?* |

Roles are bound to permissions using Keycloak **composite roles**, so the token carries *permissions*, not
just job titles. **Endpoints are guarded by permissions, never by roles.**

### Why not `[Authorize(Roles = "...")]`

Role checks encode job titles where the code means capabilities. When `support-agent` gains the ability to
issue refunds, a role-based codebase requires finding and editing every attribute that should now include it —
and the ones you miss fail **silently, in the permissive direction**, which is the worst possible failure mode
for authorisation.

Permission-based policies invert this. The endpoint declares the capability it needs and never changes:

```csharp
app.MapPost("/orders/{id}/refund", RefundOrder)
   .RequirePermission(Permissions.Order.Refund);
```

Which roles hold `order:refund` is *configuration*, in Keycloak. Granting it to support agents is a composite
role change and no deployment at all.

### Resource-based authorization

Some rules cannot be expressed over claims alone: *a customer may read only their own orders*. That depends on
the resource being accessed, not just the caller, which is exactly why ASP.NET Core has a separate
resource-based authorization API. Implemented as an `IAuthorizationHandler` over the order.

### The UI hides; the server enforces

The admin panels hide actions the token does not permit, and route guards prevent navigation to pages the user
cannot use. **That is user experience, not security.** Anyone can call the API directly with the same token,
so every permission enforced in the UI is independently enforced in the service.

This is asserted, not assumed: the authorization test suite calls protected endpoints with a
lower-privileged token and requires rejection.

### Keycloak Authorization Services / UMA — considered and not used

Keycloak can act as a full policy decision point, evaluating fine-grained resource permissions server-side via
UMA. Genuinely more powerful, and the right answer when permissions depend on runtime data too complex for a
token.

Not used here because it adds a network call to the authorisation path of every request, moves policy out of
the codebase into a console where it is harder to review and version, and is substantially heavier than this
domain needs. Being able to say *why you did not use it* is the more useful answer.

### Token size

Composite roles expand into the token, so every permission a user holds is a claim. With a handful of roles
that is fine; with hundreds of fine-grained permissions, tokens grow past comfortable header limits — some
proxies default to an 8 KB cap — and the cost is paid on every request to every service. Mitigations, in
order of preference: keep permissions coarse enough to be countable; scope them per client so a token only
carries what that application needs; and only as a last resort, look them up server-side.

## What Phase 2 adds here

- The full role → permission matrix, every cell filled
- Group design and what groups are for that roles are not
- The Keycloak client list with redirect URIs, scopes, and audience configuration
- Endpoint-by-endpoint and UI-action-by-UI-action guard tables
- Seed users for every role, with credentials, and what each one can demonstrate
