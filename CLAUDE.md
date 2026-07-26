# CLAUDE.md

Project instructions for Claude Code. Read at the start of every session.

---

## What this is

A reference .NET microservices e-commerce platform whose **primary purpose is teaching and interview
preparation**, not shipping. It demonstrates every concept in Microsoft's
[.NET Microservices guide](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/).

**Correctness and explicit reasoning matter more than speed.** Every non-trivial decision is argued in an ADR
before the code is written. Do not cut corners with `TODO` stubs on core patterns.

**The reader may be new to microservices.** Explanations should assume no prior knowledge of jargon — see
[`docs/concepts-explained.md`](docs/concepts-explained.md) for the register to write in.

---

## Hard rules — these block a merge

1. **`main` must always be a state where `docker compose up` works.** CI enforces this with a job that boots
   all 27 containers and waits for health.
2. **Documentation ships in the same PR as the code it describes.** A code-only PR is incomplete. See the
   table in [`CONTRIBUTING.md`](CONTRIBUTING.md).
3. **React and Angular move in lockstep.** Every UI feature lands in *both* frameworks in the *same* PR,
   proven by one Playwright suite run against both. Never build React first and port later
   ([ADR-0014](docs/adr/0014-react-and-angular-in-lockstep.md)).
   **Each app is self-contained** — no shared package; `react-store` and `angular-store` each own their
   permissions, auth, formatting, API client and design tokens
   ([ADR-0018](docs/adr/0018-self-contained-frontends.md)). A change to shared logic must be applied
   **twice**, and the e2e suite is what catches it if you forget.
7. **Everything must run on Docker *and* be portable to Azure**
   ([ADR-0017](docs/adr/0017-cloud-portable-architecture.md)). No service may reference an Azure SDK
   directly; provider selection happens once, in the composition root.
4. **Never commit real secrets.** Committed dev fixtures must be prefixed `dev_only_` and be worthless
   outside a throwaway local container ([ADR-0009](docs/adr/0009-secrets-management.md)).
5. **Endpoints are guarded by permissions, never roles** — `RequirePermission(Permissions.Order.Refund)`,
   not `[Authorize(Roles = "admin")]` ([`docs/authorization-model.md`](docs/authorization-model.md)).
6. **No service project may reference another service project**, and only `EventBus.RabbitMQ` may reference
   `RabbitMQ.Client`. Asserted by `tests/unit/ECommerce.Architecture.Tests` — it breaks the build.

---

## Working agreement

- Work **one phase at a time**; open a PR at the end of each and **stop for review**.
- Branch per phase: `phase/NN-short-name`. Branch from the previous phase branch if its PR is not yet merged.
- **Conventional Commits**, scoped: `feat(ordering): add Order aggregate root with invariants`.
- Commit in small increments and **push after each meaningful unit of work**. Never squash a phase into one
  commit — the history is part of the deliverable.
- At the end of each phase, state the concepts now demonstrated and the interview questions the code answers.

### Phase plan

| Phase | Scope | Status |
|-------|-------|--------|
| 1 | Repo, solution skeleton, building blocks, compose, CI | ✅ merged |
| 2 | Keycloak realm, `Auth` building block, authorization model | ✅ merged |
| 3 | **Both** storefront shells with OIDC login; self-contained frontends (ADR-0018) | ✅ merged |
| 4 | Catalog + Storefront BFF + browse/search/detail — **both frameworks** | ✅ merged |
| 5 | User Profile + My Account (profile, addresses, preferences) — **both** | ✅ 34 e2e specs green on both |
| 6 | Basket + Ordering (DDD/CQRS) + outbox + cart/checkout — **both** | ⬜ |
| 7 | Payment + Inventory + Notification + Saga | ⬜ |
| 8 | Back-office + Admin BFF + **both** admin shells | ⬜ |

Mobile (React Native) is explicitly **deferred** — the user asked not to build it yet.

---

## Commands

### .NET

> ⚠️ The SDK is a **per-user install** and is **not on `PATH`**. Prefix every session:

```powershell
$env:PATH="$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
```

```powershell
dotnet build ECommerce.slnx                          # 23 projects, warnings-as-errors
dotnet test ECommerce.slnx                           # unit + architecture
dotnet test tests/integration/ECommerce.Auth.IntegrationTests   # real Keycloak container
dotnet format ECommerce.slnx --verify-no-changes     # CI enforces this
```

The solution is **`.slnx`** (the .NET 10 XML format), not `.sln`.

### Stack

```powershell
cd deploy
docker compose up -d --wait --wait-timeout 420   # --wait fails if a service crashes after starting
docker compose ps
docker compose logs -f catalog-api
docker compose down -v                            # DELETES all data
```

### Web

```powershell
cd web
npm install                                       # npm workspaces - run from web/, not a sub-package
npm run build --workspace react-store
npm run build --workspace angular-store
npm test  --workspace react-store                 # 20 unit tests
npm test  --workspace angular-store               # the SAME 20 - drift guard
node ../scripts/check-design-tokens.mjs           # contrast + cross-app palette drift
```

### End-to-end (the parity proof)

```powershell
cd tests/e2e
npm run test:react                                # 34 specs against :3000
npm run test:angular                              # the SAME 34 against :4200
```

Both must pass. Specs use roles and accessible names only - never CSS selectors or test ids - because they
run against two independent implementations.

### Demo helper

```powershell
./scripts/show-permissions.ps1                    # every seed user and their token permissions
./scripts/show-permissions.ps1 -User administrator
```

---

## Environment gotchas — learned the hard way, do not rediscover

| Gotcha | What happens | Fix |
|---|---|---|
| **PowerShell 5.1 mangles UTF-8** | `Get-Content -Raw` + `Set-Content` on a UTF-8 file without BOM turns em-dashes into `â€"` | Use `[System.IO.File]::ReadAllText/WriteAllText` with `UTF8Encoding($false)`, or the Edit tool |
| **PowerShell here-strings break on `"`** | `git commit -m @'...'@` containing a double quote splits the message into pathspecs | Write the message to a file and use `git commit -F` |
| **`Set-Content -Encoding utf8` adds a BOM** | `dotnet format` fails with `error CHARSET` | Strip BOMs, or write via the Write tool |
| **Postgres 18 changed its data dir** | Container refuses to start, message *looks* like data corruption | Mount at `/var/lib/postgresql`, **not** `/var/lib/postgresql/data` |
| **Keycloak rejects unknown JSON fields** | Realm import fails with `Unrecognized field "_comment"` | No comments in `realm-export.json`; explanation goes in the docs |
| **Keycloak `description` is capped at 255 chars** | Import fails with `value too long for type character varying(255)` | Keep client descriptions short |
| **Keycloak 26 uses `defaultRole` (object)** | Not the old `defaultRoles` array | See the realm export |
| **Seq requires an explicit auth decision** | Container crash-loops on startup | `SEQ_FIRSTRUN_NOAUTHENTICATION=true` in dev |
| **Seq image has `curl`, not `wget`** | Healthcheck fails silently | Use `curl -fsS` |
| **`setup-python` cache needs a requirements file** | Docs workflow fails before it starts | `docs/requirements.txt` exists for this |
| **Central Package Management** | `NU1109` downgrade errors when a transitive package is newer | Bump the version in `Directory.Packages.props`; never add `Version=` to a `PackageReference` |
| **Issuer vs metadata address** | Every token rejected, though both URLs are the same server | `Auth__Issuer` = the URL the **browser** used (`localhost`); `Auth__MetadataAddress` = the internal one (`keycloak:8080`) |
| **Keycloak imports the realm only on FIRST start** | Editing `realm-export.json` and restarting changes nothing | `docker compose rm -sf keycloak keycloak-db` + `docker volume rm ecommerce_keycloak-db-data`, then `up -d --wait keycloak` |
| **A re-imported realm has NEW signing keys** | Every request `401` afterwards; services cached the old JWKS at startup | `docker compose restart catalog-api user-profile-api storefront-bff` — this is exactly what an IdP key rotation looks like |
| **EF infers entity state from the key** | `DbUpdateConcurrencyException: expected to affect 1 row(s), but actually affected 0` on an INSERT | The domain sets `Guid.CreateVersion7()` in constructors, so a non-default key reads as "exists". Mark every such key `ValueGeneratedNever()` |
| **A snake_case naming convention breaks owned types** | `OwnsOne` mapping fails — the shadow key must match the owner's PK column | Only apply the convention where hand-written SQL needs it (Catalog); not in EF-only services |
| **Serilog `MinimumLevel.Override` must precede `ReadFrom.Configuration`** | `Serilog__MinimumLevel__Override__*` env vars silently do nothing — you debug blind | Already fixed in `ObservabilityExtensions.cs`; do not reorder |
| **Playwright `getByLabel` is a substring match** | `getByLabel('Email')` also matches "Email me about my orders" | Pass `{ exact: true }` |
| **Vitest does not type-check** | esbuild strips types, so a test calling a function with the wrong argument shape passes | React's `npm run build` (`tsc -b`) covers specs; Angular's `ng test` type-checks them itself. Run the build, not just the tests |
| **`ng test` fails when there are zero specs** | `No tests found matching **/*.spec.ts` - a red CI job with nothing wrong | Keep at least one real spec in `angular-store` |
| **CI Node cache needs the workspace lockfile** | `Some specified paths were not resolved, unable to cache dependencies` before anything compiles | `cache-dependency-path: web/package-lock.json` - there is one lockfile, at `web/`, not one per app |

---

## Conventions

### Code

- `.editorconfig` is the single source of truth for style **and** analyzer severity. Warnings are errors.
- Suppress a diagnostic only in `.editorconfig`, **with a comment saying why**. Never relax
  `TreatWarningsAsErrors`.
- Nullable reference types are escalated to **errors**.
- Domain projects reference **nothing** outside the BCL — no EF Core, no MediatR, no ASP.NET.
- Hand-written mappers, not AutoMapper ([ADR-0015](docs/adr/0015-manual-mappers-over-automapper.md)).

### Comments and docs

Explain **why**, not what. Every file implementing a non-obvious pattern carries a header comment naming the
pattern and linking to the doc page. Every doc page states what, why, how, and **the alternatives rejected**.

An ADR with no downsides is marketing — always include what the decision costs.

### Diagrams

**Mermaid committed as text**, never images. Update the diagram in the same commit as the change that
invalidates it.

---

## Key files

| File | Why it matters |
|------|----------------|
| [`docs/concepts-explained.md`](docs/concepts-explained.md) | Plain-English guide; sets the register for all explanation |
| [`docs/architecture.md`](docs/architecture.md) | C4 model, service catalogue, sync/async rules |
| [`docs/domain/bounded-contexts.md`](docs/domain/bounded-contexts.md) | Why each boundary sits where it does |
| [`docs/authorization-model.md`](docs/authorization-model.md) | Role/permission matrix |
| [`docs/adr/`](docs/adr/) | 16 ADRs. **Immutable once merged** — supersede, never edit |
| [`web/parity-checklist.md`](web/parity-checklist.md) | React/Angular parity tracking |
| [`identity/keycloak/realm-export.json`](identity/keycloak/realm-export.json) | The realm, imported on startup |
| [`Directory.Packages.props`](Directory.Packages.props) | One version per package, solution-wide |

---

## Seed users

All password `Passw0rd!`. Keycloak admin: `admin` / `dev_only_kc_admin_pw` at http://localhost:8080

| User | Role | Perms |
|------|------|:---:|
| `customer` | `customer` | 5 |
| `support` | `support-agent` | 6 |
| `catalogmgr` | `catalog-manager` | 7 |
| `ordermgr` | `order-manager` | 9 |
| `administrator` | `admin` | 17 |
| `blocked` | `customer` | disabled — cannot log in |

---

## Endpoints

| Surface | URL |
|---------|-----|
| Keycloak | http://localhost:8080 |
| Seq (logs) | http://localhost:8081 |
| Jaeger (traces) | http://localhost:16686 |
| RabbitMQ | http://localhost:15672 (`ecom` / `dev_only_rabbit_pw`) |
| Services REST | 5001–5009 · gRPC 5101–5107 |
| BFFs | 6001 storefront · 6002 admin · 6003 mobile |
| Web (Phase 3+) | 3000 react-store · 4200 angular-store · 3001/4201 admin |
| Docs site | https://rakshins10.github.io/E-Commerce/ |

---

## Communicating with this user

- **Lead with what changed and what to do**, not with process narration.
- Keep it short. They have said more than once that long responses are hard to follow.
- Explain jargon on first use, or link to `docs/concepts-explained.md`.
- Be explicit about what is **not** built yet — they have been surprised by this before.
- When something fails, say so plainly with the actual error, then fix it.
