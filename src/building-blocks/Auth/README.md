# ECommerce.Auth

**Arrives in Phase 2**, alongside the Keycloak realm it validates against.

Security must be uniform across nine services. Reimplementing token validation per service is how services
drift, and drift in authorisation code is how holes appear — one service forgetting to validate the audience
is indistinguishable from the others until someone replays a token minted for a different client.

## Planned contents

| Component | Responsibility |
|-----------|----------------|
| `AddJwtAuthentication()` | JWT bearer validation against the realm's JWKS from the OIDC discovery document — signature, issuer, lifetime, and **audience**. Audience validation is the check most often omitted, and omitting it means a token issued for a different client is accepted. |
| `PermissionRequirement` / `PermissionHandler` | Policy-based authorization over fine-grained permissions (`catalog:write`, `order:refund`) rather than role checks. |
| `ResourceOwnerHandler` | Resource-based authorization — a customer may read only *their own* orders. Cannot be expressed as a policy over claims alone, because it depends on the resource being accessed. |
| `ICurrentUser` | The `sub`, roles, and permissions of the caller, injected rather than dug out of `HttpContext` at each call site. |
| `Permissions` | Constants for every permission string, so a typo is a compile error rather than a silently-failing authorisation check. |

## Why permissions and policies rather than `[Authorize(Roles = "...")]`

Role checks scatter policy across the codebase and encode *job titles* where the code means *capabilities*.
When `support-agent` gains the ability to issue refunds, a role-based codebase requires finding and editing
every `[Authorize(Roles = ...)]` that should now include it — and the ones you miss fail silently in the
permissive direction.

Permission-based policies invert that: the endpoint declares the capability it needs
(`RequirePermission("order:refund")`) and never changes. Which roles hold that permission is configuration, in
Keycloak, expressed with **composite roles** — so the token carries permissions, not just titles.

Full design, including the complete role/permission matrix and the token-size implications of stuffing many
permissions into a JWT, lands in [`docs/authorization-model.md`](../../../docs/authorization-model.md).
