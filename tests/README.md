# Tests

Four suites, each answering a different question.

| Suite | Question | Runs against | Speed |
|-------|----------|--------------|-------|
| [`unit/`](unit/) | Is this logic correct? | Nothing external | milliseconds |
| [`integration/`](integration/) | Does it work against real infrastructure? | Real Postgres, RabbitMQ, Redis, **Keycloak** in Testcontainers | seconds |
| [`contract/`](contract/) | Do two services still agree? | Recorded contracts | fast |
| [`e2e/`](e2e/) | Does the user journey work? | The whole stack, **twice** - React and Angular | minutes |

## Why integration tests use real containers, not mocks

Mocking a database proves your code calls the mock the way you told the mock to expect. It cannot catch a
migration that does not apply, a query whose SQL is invalid, a `SKIP LOCKED` that does not behave as assumed,
or an Npgsql `DateTime` kind mismatch - which are the failures that actually happen.

**Testcontainers** starts real dependencies per test class and tears them down after. Docker is the only
prerequisite, and nothing is left behind.

This matters most for the **Keycloak** test: a real container, a real login, a real signed token flowing
through a real service. Hand-forging a JWT to test authorisation proves nothing about issuer validation,
audience validation, or JWKS retrieval - precisely the parts most likely to be wrong.

## The architecture tests

[`unit/ECommerce.Architecture.Tests`](unit/ECommerce.Architecture.Tests/) enforces boundary rules a monorepo
cannot enforce physically ([ADR-0008](../docs/adr/0008-monorepo.md)):

- No service project references another service project
- No service references `RabbitMQ.Client` directly - only `IEventBus`
- The Ordering domain project references nothing outside the BCL
- Read-side query code never touches domain types

A convention nobody checks is a convention nobody follows. These break the build instead.

## Authorization tests

Phase 2 onward. For every protected endpoint, a **lower-privileged token must be rejected**. This is what
proves the server does not rely on the UI having hidden the button -
[`docs/authorization-model.md`](../docs/authorization-model.md) explains why that distinction is the whole
point.

## What there is

| Suite | Where | Count |
|---|---|---:|
| Domain invariants (no database) | `tests/unit/ECommerce.Ordering.Domain.Tests` | 28 |
| Shared building blocks | `tests/unit/ECommerce.Common.Tests` | 17 |
| Architecture boundaries | `tests/unit/ECommerce.Architecture.Tests` | 4 |
| Auth, against a real Keycloak container | `tests/integration/ECommerce.Auth.IntegrationTests` | 16 |
| Storefront end-to-end, **run twice** - React and Angular | `tests/e2e/specs` | 64 |
| Back-office end-to-end, **run twice** | `tests/e2e/specs-admin` | 26 |
| Frontend unit, the same 25 assertions in each storefront | `web/*/src` | 25 |

---

## Running them

```bash
dotnet test ECommerce.slnx                              # unit + integration + contract
dotnet test tests/unit/ECommerce.Architecture.Tests     # boundary rules only
```

The e2e suite needs the stack up, and each command runs the **same specs** against a different app.
That duplication is the parity proof, not an accident
([ADR-0014](../docs/adr/0014-react-and-angular-in-lockstep.md)):

```bash
cd tests/e2e
npm run test:react          # 64 storefront specs against :3000
npm run test:angular        # the SAME 64 against :4200
npm run test:react:admin    # 26 back-office specs against :3001
npm run test:angular:admin  # the SAME 26 against :4201
```

> **Run a suite twice before trusting it.** These specs place real orders and edit real catalogue rows,
> so one that passes on a clean database and fails on the second run is a spec that assumed its starting
> state. Several did, once.

---

## Coverage, measured and stated plainly

`dotnet test --collect:"XPlat Code Coverage"` (Phase 13, 2026-08-18):

| Assembly | Line coverage | Covered by |
|---|---:|---|
| ECommerce.Ordering.Domain | 83% | 28 domain tests - every invariant |
| ECommerce.Auth | 81% | 16 integration tests against a real Keycloak |
| ECommerce.Common | 62% | 17 unit tests |
| Everything else | ~0% unit | **The 180 e2e specs**, deliberately |

The last row is a strategy, not a gap: API endpoints, the outbox, the saga and both front ends are
exercised through the running system, where the failure modes actually live - serialization, wiring,
idempotency under real at-least-once delivery. Unit-testing a minimal-API handler against a mocked
DbContext would raise the number without raising confidence. The domain, where the invariants live, is
where line coverage is held high.

---

## Repository hygiene

Two checks that fail in seconds rather than after a twenty-minute build, and run as their own CI job:

```bash
node scripts/check-ascii-punctuation.mjs   # no em dashes, en dashes or curly quotes
node scripts/check-design-tokens.mjs       # WCAG contrast + cross-app palette drift
node scripts/check-doc-links.mjs           # every relative markdown link resolves
```
