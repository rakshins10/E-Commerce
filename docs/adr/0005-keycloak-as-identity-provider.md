# ADR-0005: Keycloak as the identity provider

- **Status:** Accepted
- **Date:** 2026-07-25
- **Phase:** 1

## Context

Five client applications (two React, two Angular, one React Native), three BFFs, and nine services all need
a single, consistent answer to "who is this caller and what may they do". The system needs:

- Interactive login for browser SPAs and a mobile app — **Authorization Code + PKCE**, since none of them
  can hold a secret.
- Service-to-service authentication — **client credentials**.
- Roles, fine-grained permissions, and groups, carried in a token that every service can validate offline.
- Refresh-token rotation and silent renew.
- A programmatic admin surface, because the admin panel must manage users.
- Configuration as code, so `docker compose up` produces a fully configured realm with **no click-ops**.

The first question is not *which* provider — it is whether to build one at all.

## Why not build our own

Because authentication is a **generic subdomain**: every business needs it, no business wins by doing it
better, and doing it slightly wrong is catastrophic rather than merely annoying. Building it means owning,
correctly and forever:

password hashing with a modern KDF and a migration path as parameters age · credential-stuffing and
brute-force defence · account lockout that resists enumeration · MFA (TOTP, WebAuthn) · email verification
and password reset flows that are not themselves an attack vector · session management and revocation ·
token signing, key storage, and key rotation · correct OIDC/OAuth2 implementation including PKCE and the
several ways to get it subtly wrong · consent and audit trails.

Every item is a well-known CVE class. None of it differentiates an online store. **The strongest argument
for an external IdP is not that it is cheaper to integrate — it is that "we wrote our own auth" is a finding
in every security review, and correctly so.**

## Options considered

| | **Keycloak** | **Microsoft Entra ID** | **Auth0 / Okta** | **Duende IdentityServer** |
|---|---|---|---|---|
| Model | Self-hosted OSS product | Managed SaaS | Managed SaaS | **Library** you host in your own ASP.NET app |
| Cost | Free; you pay in ops | Free tier, then per-MAU; B2C priced separately | Free tier, then per-MAU — gets expensive fast | **Commercial licence** (free only under a revenue threshold) |
| Runs in `docker compose` | **Yes** | No — cloud only | No — cloud only | Yes, but you write the host |
| Config as code | **Realm JSON import** | Bicep/Terraform/Graph | Terraform / Management API | C# |
| Roles / composites / groups | **Rich, built in** | App roles + groups; composites need Graph work | Roles + permissions (RBAC add-on) | Whatever you implement |
| Admin REST API | **Comprehensive** | Microsoft Graph | Management API | You build it |
| Work to own | Upgrades, DB, HA, hardening | Almost none | Almost none | **You own the security-critical code** |
| Best when | Self-hosting, no vendor lock-in, offline dev | Already in the Microsoft estate; workforce identity | Fast time-to-market, budget for it | Deep customisation, .NET-native, licence acceptable |

### Why not each of the alternatives

**Entra ID** would be the pragmatic choice for an enterprise already on Microsoft 365 — workforce identity,
conditional access, and device compliance come free. It is disqualified *here* by one hard requirement:
it cannot run in a container. A cloud-only IdP means no offline development, a tenant to provision before
anyone can run the repo, and integration tests that depend on a shared external system. It also splits
awkwardly between Entra ID (workforce) and Entra External ID (customers), and the customer-facing product's
role model is weaker than Keycloak's composites.

**Auth0** has the best developer experience of the four and would be a sound production choice for a startup.
Rejected for the same containerisation reason, plus per-MAU pricing that punishes exactly the consumer-scale
traffic an e-commerce site generates, and a free tier too small to model this many clients and roles.

**Duende IdentityServer** is the most .NET-native option and the most instructive to configure — it is a
library, so the OIDC flows are visible in your own code. Two problems. Commercially it requires a paid
licence above a revenue threshold, which is a genuine consideration for a real product. More importantly it
is a **library, not a product**: it gives you protocol endpoints, but you still build user storage, the
login UI, password reset, MFA, the admin console, and the user-management API. That is most of the work, and
it is the security-critical part. Choosing it means partially reversing the "do not build your own auth"
decision.

**Keycloak** is the only option that satisfies every hard requirement: it runs in a container, the entire
realm — clients, roles, composite roles, groups, and seed users — is expressed as **one JSON file committed
to the repo and imported on startup**, it has a comprehensive Admin REST API for the back-office, and its
composite-role model maps cleanly onto the permission design in
[authorization-model.md](../authorization-model.md). It is also an honest reflection of the enterprise
market, where Keycloak (and its Red Hat SSO packaging) is very widely deployed.

## Decision

**Keycloak, self-hosted as a container, configured entirely by a committed `realm-export.json` imported at
startup.**

Flows used:

| Caller | Flow | Client type |
|--------|------|-------------|
| React/Angular storefronts and admin panels | Authorization Code + PKCE | **Public** — no secret |
| React Native app | Authorization Code + PKCE via the **system browser** | **Public** |
| BFFs, back-office, saga (service-to-service) | Client credentials | **Confidential** |

**Public clients hold no secret**, because a secret shipped in a JavaScript bundle or an app binary is not a
secret. PKCE replaces it: the client proves possession of a verifier it generated, so an intercepted
authorization code is useless to an attacker. The mobile app uses the *system browser* rather than an
embedded webview, because a webview lets the host application observe credentials and defeats SSO — this is
the substance of RFC 8252.

**Tokens are validated at the gateway *and* again at each service**, as defence in depth. The gateway is not
a trust boundary we are willing to bet everything on: anything reaching a service directly — a
misconfiguration, a future internal caller, a compromised pod — must still be authenticated. Both validate
signature via the realm's JWKS from the discovery document, plus issuer, expiry, and **audience**. Audience
validation is the one most often omitted, and omitting it means a token minted for a different client is
happily accepted.

**Each application gets its own Keycloak client** (`storefront-react`, `storefront-angular`, `admin-react`,
`admin-angular`, `mobile`, plus confidential clients for the BFFs and services). Separate clients mean
separate redirect URIs, separate scopes, and the ability to disable one compromised application without
touching the others.

## Consequences

### What this buys us
- No authentication code of our own — the entire class of credential-handling bugs is out of scope.
- The whole system, fully configured with seed users for every role, comes up with `docker compose up`.
  Nobody clicks through an admin console to make the repo work.
- Realm configuration is reviewable in pull requests and diffs like code.
- Integration tests can spin up a real Keycloak in Testcontainers and assert against a **real token** rather
  than a hand-forged one.
- Composite roles let the token carry *permissions*, not just job titles — see
  [authorization-model.md](../authorization-model.md).

### What this costs us
- **We now operate an IdP.** Upgrades, its database, backups, and hardening are ours. In production this
  means HA, a properly tuned database, and a rotation plan for signing keys — non-trivial, and the honest
  price of not using SaaS.
- **Startup time.** Keycloak takes ~30–60 s to import the realm and become ready. Every dependent service
  must wait on a real readiness check rather than a fixed sleep — an infrastructure detail that shows up
  immediately in the compose file and in CI.
- **The issuer-mismatch trap.** Inside the compose network Keycloak is `http://keycloak:8080`; to the
  browser it is `http://localhost:8080`. The `iss` claim is whatever the browser used, so a service
  validating against the internal hostname rejects every token. Solved by pinning `KC_HOSTNAME` to one
  public URL used by both. This is the single most common Keycloak-in-Docker failure and is documented in
  [getting-started.md](../getting-started.md#troubleshooting).
- **Realm JSON is large and hand-editing it is error-prone.** It is generated by export and reviewed, not
  authored freehand.
- **Committed dev credentials** need an unmissable warning so nobody promotes them. Handled in
  [ADR-0009](0009-secrets-management.md).

### What we will have to revisit
If this became a real product inside a Microsoft-estate enterprise, Entra ID would likely win on total cost
of ownership — the operational burden of self-hosting an IdP is easy to underestimate. The anticorruption
layer in Back-office over the Keycloak Admin API exists partly to keep that door open: swapping providers
should touch one adapter and the realm configuration, not the services.

## References

- [.NET microservices guide — securing microservices](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/secure-net-microservices-web-applications/)
- [RFC 7636 — PKCE](https://datatracker.ietf.org/doc/html/rfc7636) · [RFC 8252 — OAuth 2.0 for Native Apps](https://datatracker.ietf.org/doc/html/rfc8252)
- [ADR-0004](0004-identity-vs-profile-data-split.md) — what Keycloak deliberately does *not* store
- [authorization-model.md](../authorization-model.md) — the role and permission design
