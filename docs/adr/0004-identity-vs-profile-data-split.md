# ADR-0004: Split identity data (Keycloak) from profile data (User Profile service)

- **Status:** Accepted
- **Date:** 2026-07-25
- **Phase:** 1

## Context

[ADR-0005](0005-keycloak-as-identity-provider.md) delegates authentication to Keycloak. That immediately
raises a question with a non-obvious answer: **where does the rest of "the user" live?**

A user in this system has: credentials, roles, and group membership; and also a display name, saved
shipping addresses, a preferred locale and currency, a theme, marketing opt-ins, notification channel
preferences, a wishlist, and GDPR consent records.

Keycloak can technically store all of it. It has user attributes, and those attributes can be mapped into
token claims. Many teams do exactly this and regret it. Equally, we could keep a full user table in our own
service including credentials, which is the mistake Keycloak exists to prevent.

The two datasets look similar — they are both "about the user" — which is precisely why the boundary needs
to be argued rather than assumed.

## Options considered

### Option A — Everything in Keycloak user attributes
One store, no synchronisation, no join key to manage. Attributes are queryable through the Admin API and
can be mapped into claims.

Where it breaks down:
- **Token bloat.** Mapping addresses and preferences into claims puts them in the Authorization header of
  every single request to every service. Tokens grow past comfortable header limits, and the cost is paid on
  every request by services that do not care.
- **No validation or modelling.** Keycloak attributes are string key-value pairs. "A user may have at most
  one default shipping address" is not expressible.
- **Awkward querying.** "All users in the UK who opted into marketing" is not a query the Admin API is built
  for.
- **Vendor lock-in of business data.** Replacing the IdP becomes a data migration of core business data
  rather than a configuration change.
- **Wrong lifecycle.** GDPR erasure of marketing preferences and deactivation of credentials are different
  operations with different rules; conflating them makes both harder.

### Option B — Everything in our own service, including credentials
Full control, one model, easy to query and validate.

Rejected outright: it reintroduces password hashing, credential-stuffing defence, MFA, session management,
token signing and rotation, and account recovery — the entire class of problems delegated in
[ADR-0005](0005-keycloak-as-identity-provider.md), and the class with the worst consequences when done
slightly wrong.

### Option C — Split by responsibility, joined on `sub`
Keycloak owns authentication and authorisation data. A `user-profile` service owns everything else, keyed by
the Keycloak `sub` claim.

## Decision

**Split the data by responsibility. Keycloak answers "who are you, and what may you do". The User Profile
service answers "what do we know about you". The `sub` claim is the only join key.**

The split is not arbitrary — the two datasets differ on every axis that matters:

| | Identity data (Keycloak) | Profile data (User Profile) |
|---|---|---|
| Changes | rarely; security-sensitive | often; user-driven, self-service |
| Read by | the auth layer, on every request | the storefront, per page |
| Consequence of corruption | account takeover | wrong theme |
| Belongs in the token | yes — `sub`, roles, permissions | **no** — it would bloat every request |
| Regulatory character | credentials; auditable, retained | personal data; erasable under GDPR |
| Portability | tied to the IdP | tied to the business |
| Modelling needs | fixed, standardised (OIDC) | rich, evolving, validated |

**The test to apply at the boundary:** *is this needed to make an authorisation decision?* If yes, it is
identity data and belongs in the token. If no, it is profile data and is fetched when needed. A shipping
address never decides whether a request is allowed, so it never belongs in a JWT.

### Why `sub` and not email or username

`sub` is opaque, immutable, and never reused. Email changes and is re-assignable; username may be editable.
Keying business data on a mutable identifier guarantees an eventual orphaning incident. This is worth
stating because using email as the key is a common and quietly destructive shortcut.

### Provisioning: event-driven, not synchronous

A profile is created on first login, driven by an event rather than a synchronous call in the login path.
Two reasons, in order of importance:

1. **A slow or failed profile service must never block a login.** Putting a call to our service inside the
   authentication path makes authentication only as available as the least available thing it touches.
2. It keeps the dependency pointing the right way — Keycloak knows nothing about us.

The profile is created lazily and idempotently: the first request carrying an unrecognised `sub` triggers
provisioning, and re-provisioning the same `sub` is a no-op. Idempotency is required because the trigger may
fire more than once — see [ADR-0010](0010-transactional-outbox.md) on at-least-once delivery.

## Consequences

### What this buys us
- Tokens stay small. Only `sub` and authorisation claims travel on every request.
- Profile data gets a real domain model with real invariants ("exactly one default shipping address",
  "consent records are append-only") enforced in code that can be tested.
- Replacing Keycloak with Entra ID becomes a configuration change plus one anticorruption adapter, not a
  migration of business data.
- Clean GDPR story: erasure of profile data and deactivation of credentials are separate, independently
  auditable operations.
- Login availability is not coupled to our service's availability.

### What this costs us
- **Two places to look.** Answering "tell me everything about this user" requires both Keycloak and the
  profile service. The Back-office service exists partly to compose that view, and its user-detail screen is
  an aggregation across two sources.
- **A referential gap.** A user deleted in Keycloak leaves an orphaned profile unless something reconciles
  it. Handled by consuming Keycloak's user-deletion event; that path must be tested, because it is exactly
  the kind of edge that rots silently.
- **Eventual consistency on provisioning.** There is a window — milliseconds in practice — where a valid
  token exists but the profile does not. Every profile read must therefore tolerate "not yet provisioned"
  rather than assuming presence.
- **The join is manual.** No foreign key spans the two stores; the discipline is enforced by convention and
  tests, not by the database.

### What we will have to revisit
If admin screens grow to need heavy cross-cutting queries over identity *and* profile data together
("all disabled users with an open order"), the aggregation cost in Back-office will start to hurt. The
answer then is a read model in Back-office fed by events from both sides — not moving profile data into
Keycloak.

## References

- [domain/bounded-contexts.md — Customer Profile](../domain/bounded-contexts.md#customer-profile)
- [ADR-0005](0005-keycloak-as-identity-provider.md) — the decision that makes this one necessary
- [authorization-model.md](../authorization-model.md) — what actually is in the token
