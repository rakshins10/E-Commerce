# Tests

Four suites, each answering a different question.

| Suite | Question | Runs against | Speed |
|-------|----------|--------------|-------|
| [`unit/`](unit/) | Is this logic correct? | Nothing external | milliseconds |
| [`integration/`](integration/) | Does it work against real infrastructure? | Real Postgres, RabbitMQ, Redis, **Keycloak** in Testcontainers | seconds |
| [`contract/`](contract/) | Do two services still agree? | Recorded contracts | fast |
| [`e2e/`](e2e/) | Does the user journey work? | The whole stack, **twice** — React and Angular | minutes |

## Why integration tests use real containers, not mocks

Mocking a database proves your code calls the mock the way you told the mock to expect. It cannot catch a
migration that does not apply, a query whose SQL is invalid, a `SKIP LOCKED` that does not behave as assumed,
or an Npgsql `DateTime` kind mismatch — which are the failures that actually happen.

**Testcontainers** starts real dependencies per test class and tears them down after. Docker is the only
prerequisite, and nothing is left behind.

This matters most for the **Keycloak** test: a real container, a real login, a real signed token flowing
through a real service. Hand-forging a JWT to test authorisation proves nothing about issuer validation,
audience validation, or JWKS retrieval — precisely the parts most likely to be wrong.

## The architecture tests

[`unit/ECommerce.Architecture.Tests`](unit/ECommerce.Architecture.Tests/) enforces boundary rules a monorepo
cannot enforce physically ([ADR-0008](../docs/adr/0008-monorepo.md)):

- No service project references another service project
- No service references `RabbitMQ.Client` directly — only `IEventBus`
- The Ordering domain project references nothing outside the BCL
- Read-side query code never touches domain types

A convention nobody checks is a convention nobody follows. These break the build instead.

## Authorization tests

Phase 2 onward. For every protected endpoint, a **lower-privileged token must be rejected**. This is what
proves the server does not rely on the UI having hidden the button —
[`docs/authorization-model.md`](../docs/authorization-model.md) explains why that distinction is the whole
point.

## Running them

```bash
dotnet test ECommerce.slnx                              # unit + integration + contract
dotnet test tests/unit/ECommerce.Architecture.Tests     # boundary rules only
cd tests/e2e && npx playwright test                     # e2e (needs the stack up)
```
