# ECommerce.Auth

Shared authentication and authorization, applied identically by every service.

**Why shared?** Security must be uniform. Nine services each configuring token validation by hand means nine
chances to omit audience validation, nine clock-skew settings, and nine subtly different claim mappings.
When one drifts it becomes the way in — and nothing reports it, because the service still works.

Full design: [`docs/authorization-model.md`](../../../docs/authorization-model.md).
Plain-English introduction: [`docs/concepts-explained.md §17–18`](../../../docs/concepts-explained.md#17-oauth2-oidc-jwt-and-pkce).

---

## Contents

| File | Responsibility |
|------|----------------|
| [`Permissions.cs`](Permissions.cs) | Every permission as a constant, so a typo is a build error rather than a silently-failing check |
| [`AuthenticationExtensions.cs`](AuthenticationExtensions.cs) | JWT validation (signature via JWKS, issuer, lifetime, **audience**) and one policy per permission |
| [`PermissionRequirement.cs`](PermissionRequirement.cs) | Policy-based authorization over capabilities |
| [`ResourceOwnerRequirement.cs`](ResourceOwnerRequirement.cs) | Resource-based authorization — "only your own orders" |
| [`CurrentUser.cs`](CurrentUser.cs) | `ICurrentUser` — the caller's `sub`, roles and permissions, injected rather than dug out of `HttpContext` |
| [`EndpointExtensions.cs`](EndpointExtensions.cs) | `RequirePermission()` so the guard is visible at the route |

---

## Using it

```csharp
// Program.cs
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddPermissionPolicies();

app.UseAuthentication();
app.UseAuthorization();

// Endpoints declare the capability they need — never a role.
app.MapGet("/products",          GetProducts);                              // public
app.MapPost("/products",         CreateProduct).RequirePermission(Permissions.Catalog.Write);
app.MapDelete("/products/{id}",  DeleteProduct).RequirePermission(Permissions.Catalog.Delete);
app.MapGet("/orders/{id}",       GetOrder).RequireAnyPermission(
                                     Permissions.Order.Read, Permissions.Order.ReadOwn);
```

Configuration (supplied by compose):

```
Auth__Issuer            http://localhost:8080/realms/ecommerce   ← what the BROWSER used
Auth__MetadataAddress   http://keycloak:8080/realms/ecommerce/.well-known/openid-configuration
Auth__Audience          ecommerce-api
```

> **`Issuer` and `MetadataAddress` differ on purpose.** Metadata is fetched server-to-server over the
> internal network; the issuer that must match the token is the public URL the browser used. Getting this
> wrong produces "every token is rejected" while both URLs point at the same server — the single most common
> Keycloak-in-Docker failure. See [getting-started](../../../docs/getting-started.md#troubleshooting).

---

## Two things worth knowing

**Permissions, not roles.** An endpoint requiring `order:refund` never changes when the business decides
support agents may refund — that becomes a composite-role edit in Keycloak with no deployment. An endpoint
requiring `Roles = "admin,order-manager"` must be found and edited, and the ones you miss fail *permissively*,
which is the failure mode nobody notices.

**The server is the only enforcement point.** The UI hides actions the token does not permit — that is
convenience, not security, since anyone can call the API directly with the same token. Proven by
[`AuthorizationTests`](../../../tests/integration/ECommerce.Auth.IntegrationTests/AuthorizationTests.cs),
which calls protected endpoints with lower-privileged tokens and requires rejection.

---

## Tests

16 integration tests run against a **real Keycloak container** importing the same
[`realm-export.json`](../../../identity/keycloak/realm-export.json) that `docker compose` uses — so a realm
change that breaks authorization breaks the build.

```bash
dotnet test tests/integration/ECommerce.Auth.IntegrationTests
```

They deliberately weight the **negative** cases: a customer cannot write to the catalog, a support agent
cannot refund, a catalog manager cannot manage users. Confirming an admin can reach an admin endpoint is
easy; proving everyone else cannot is the assertion that matters.
