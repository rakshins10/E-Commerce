# Service reference

One page per backend service. Each is written **in the same pull request as the service it documents** — a
service page that lags its code is a bug, not a backlog item.

## What each page contains

- Purpose and bounded context, and what it deliberately does **not** own
- Domain model and aggregates
- Database schema and migrations
- Dependencies — synchronous and asynchronous
- Events published and consumed, with payload schemas
- Configuration keys
- **Complete endpoint reference**: for every endpoint the method, route, purpose, auth and required
  permission, request/response shapes, validation rules, status codes and error contract, idempotency
  behaviour, and a working `curl` example

OpenAPI is generated from the code; the written reference is kept in step with it and reviewed alongside.

## Status

| Service | Page | Arrives |
|---------|------|---------|
| Catalog | [`catalog.md`](catalog.md) | Phase 4 ✅ |
| Storefront BFF | [`storefront-bff.md`](storefront-bff.md) | Phase 4 ✅ |
| User Profile | [`user-profile.md`](user-profile.md) | Phase 5 ✅ |
| Basket | [`basket.md`](basket.md) | Phase 6 ✅ |
| Ordering | [`ordering.md`](ordering.md) | Phase 6 ✅ |
| Payment | [`payment.md`](payment.md) | Phase 7 ✅ |
| Inventory | [`inventory.md`](inventory.md) | Phase 7 ✅ |
| Notification | [`notification.md`](notification.md) | Phase 7 ✅ |
| Ordering Saga | [`ordering-saga.md`](ordering-saga.md) | Phase 7 ✅ |
| Back-office | [`back-office.md`](back-office.md) | Phase 8 ✅ |
| Admin BFF | [`admin-bff.md`](admin-bff.md) | Phase 8 ✅ |
| Mobile BFF | `mobile-bff.md` | Phase 11 |

**Everything else** exists as a deployable that boots, reports liveness and readiness, and emits traces,
metrics and logs — but has no domain yet. That baseline shape is
[`Program.cs`](../../src/services/catalog/ECommerce.Catalog.Api/Program.cs) plus
[the shared building blocks](../architecture.md#8-cross-cutting-building-blocks).
