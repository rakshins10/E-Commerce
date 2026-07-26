# Shared web layer

**Arrives in Phase 3.** Framework-neutral TypeScript consumed by the React apps, the Angular apps, **and** the
React Native app. Everything that would otherwise be written twice and then quietly diverge.

## Planned shape

```
shared/
  api/            # typed client generated from each BFF's OpenAPI document
  auth/           # OIDC config, PKCE flow helpers, token parsing, silent renew
  permissions/    # hasPermission(), permission constants, token claim extraction
  validation/     # schemas shared by forms and by the BFF contract
  formatting/     # currency, date, address, number — locale-aware
  types/          # domain types and enums mirrored from the API contract
```

## The boundary

| Belongs here | Does not |
|--------------|----------|
| What the applications **know** | What the applications **show** |
| API calls, auth, permissions, validation, formatting | Components, hooks, stores, routing, styling |
| Pure functions and types | Anything importing `react` or `@angular/core` |

An ESLint rule forbids framework imports in this package. It is not a matter of discipline: the first time a
React hook lands here, Angular can no longer consume it, and the shared layer has silently become a React
layer with extra steps.

## Why the API client is generated, not hand-written

The BFFs produce OpenAPI documents from their endpoints. Generating the client from those documents means a
backend contract change **breaks the frontend build** rather than producing a runtime `undefined` in
production. That is the same argument as gRPC internally
([ADR-0007](../../docs/adr/0007-grpc-for-internal-sync-calls.md)): where a contract crosses a boundary, make
violations a compile error.

Generation runs in CI, and a drifted checked-in client fails the build.

## `hasPermission()` and where authorisation actually happens

One implementation, used by the React permission wrapper, the Angular route guard, and the React Native
screens alike. It reads the permission claims from the parsed access token.

**The server is the only real enforcement point.** Hiding a button the user's token does not permit is *user
experience* — it stops people attempting things that will fail. It is not security, because anyone can call
the API directly with the same token. Every permission enforced in the UI is independently enforced in the
service, and [`docs/authorization-model.md`](../../docs/authorization-model.md) lists both sides for every
permission so the pair can be checked.

There is a test for this: the authorization test suite calls protected endpoints with a lower-privileged token
and asserts rejection — proving the server does not rely on the UI having hidden anything.
