# ADR-0009: Secrets stay out of source; development fixtures are labelled and worthless

- **Status:** Accepted
- **Date:** 2026-07-25
- **Phase:** 1

## Context

The repository is **public**. It also has a headline requirement that conflicts with that fact:
`docker compose up` must produce a fully working, immediately demoable system — Keycloak with a configured
realm, seed users for every role, working database and broker connections — with **no click-ops and no
manual configuration step**.

That means credentials must be present in the repository for the system to boot. The Keycloak
`realm-export.json` contains seed users with passwords. `.env.example` contains database passwords and
confidential-client secrets. There is no way to satisfy "one command to a working demo" without them.

The risk is not that these particular strings leak — they are worthless. The risk is **pattern
contamination**: a reader who sees credentials committed learns that committing credentials is acceptable
here, and the habit follows them somewhere it matters. A secret committed once is in the history forever,
and rotating it is the only real remedy.

## Options considered

### Option A — No credentials at all; a setup script generates them
Genuinely secret-free. But it breaks the one-command promise, adds a step that can fail, and makes the
Keycloak realm impossible to commit as reviewable configuration — which would forfeit
[ADR-0005](0005-keycloak-as-identity-provider.md)'s main benefit.

### Option B — Commit dev fixtures, unlabelled
What most sample repositories do. Works, and quietly teaches the wrong lesson. It also makes it impossible
for a reader to tell "this is a throwaway" from "this is a real secret someone leaked".

### Option C — Commit dev fixtures, labelled unmissably, with a hard boundary between fixture and secret
Committed values are deliberately, obviously fake, live only in files whose names say so, and every one
carries a warning. Real secrets are structurally excluded.

## Decision

**Option C.** Three rules, in order of importance:

### 1. Anything that could be a real secret is git-ignored, structurally

`.gitignore` blocks `.env` (while explicitly allowing `.env.example`), `*.pfx`, `*.pem`, `*.crt`, `*.key`,
`secrets.json`, and `appsettings.*.Local.json`. If you find yourself adding `-f` to force a commit past it,
that is the control working.

### 2. Committed development fixtures are unmistakable as fixtures

- Every value is prefixed `dev_only_` (`dev_only_pg_pw`, `dev_only_storefront_bff_secret`).
- They appear only in `deploy/.env.example` and `identity/keycloak/realm-export.json`.
- `.env.example` opens with a block stating they are development-only, not reused, and not safe to deploy.
- The root README repeats it under its own heading.
- Seed users use `Passw0rd!` — obviously a demo password, listed publicly in the README precisely *because*
  its publication is harmless.

The test applied: **if a value's publication would be harmless even to a hostile reader with the repository
in hand, it may be committed. Otherwise it may not.** Every committed value passes.

### 3. Local development secrets use .NET user-secrets

When a developer needs a real credential locally, it goes in
`dotnet user-secrets` — stored outside the repository tree in the user profile, so it cannot be committed
even by accident. Never in `appsettings.json`, never in `appsettings.Development.json` (which *is* tracked).

### The production path, documented rather than built

Stated because "how would you handle this in production?" is the immediate follow-up:

| Environment | Mechanism |
|-------------|-----------|
| Local | `dotnet user-secrets`; `deploy/.env` (git-ignored) for compose |
| CI | GitHub Actions encrypted secrets, injected as env vars, masked in logs |
| Production (Azure) | Key Vault, with the app authenticating by **managed identity** — so there is no bootstrap secret at all. Surfaced to the app either via the Key Vault configuration provider or the CSI Secrets Store driver on AKS |
| Production (K8s, cloud-agnostic) | External Secrets Operator syncing from the platform's secret store into Kubernetes Secrets; mounted as files, not env vars |

Two points worth being able to defend:

- **Managed identity is the goal**, because it removes the bootstrap problem — the classic flaw of "put the
  secret in a vault" is needing a credential to reach the vault. Platform-issued workload identity has no
  such credential.
- **Prefer files to environment variables** in production. Environment variables are inherited by child
  processes, frequently captured wholesale by crash dumps and error reporters, and visible in `/proc`.
  Compose uses env vars here because it is the ergonomic local mechanism, not because it is best practice.

Configuration reaches services only through `IConfiguration` — never a hardcoded connection string, never a
literal in a Dockerfile. That is also what keeps Kubernetes layerable later without touching code
([architecture.md §10](../architecture.md#10-what-this-system-deliberately-does-not-do)).

## Consequences

### What this buys us
- The one-command demo promise is kept.
- The Keycloak realm stays reviewable configuration-as-code, diffing like any other file.
- No real secret is committable without deliberately defeating a control.
- A reader learns the *distinction* between a fixture and a secret, which is the transferable lesson.

### What this costs us
- **The fixtures are still real credentials to a running container.** Anyone who clones and runs this gets
  a Keycloak admin login. Harmless on `localhost`; genuinely dangerous the moment someone exposes it. The
  README says so plainly, and that warning is the only thing standing between a reader and a bad outcome.
- **`.env.example` drifts.** A new variable added to compose but not to the example breaks a fresh clone. CI
  mitigates this by booting the stack from `.env.example` alone.
- **Rotation is not demonstrated.** There is no key-rotation or secret-versioning story here, because there
  are no real secrets to rotate. Named as a gap rather than pretended away.
- **`appsettings.Development.json` is tracked**, so it is one careless paste from becoming a leak. This is
  why user-secrets is the documented mechanism rather than merely the recommended one.

### What we will have to revisit
If this repository ever gained a deployed environment, this ADR is superseded immediately: fixtures get
deleted, the realm export is parameterised with placeholders resolved at deploy time, and secrets move to a
vault with managed identity before anything is exposed.

## References

- [.NET microservices guide — storing application secrets safely](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets)
- [`.gitignore`](../../.gitignore) · [`deploy/.env.example`](../../deploy/.env.example)
- [CONTRIBUTING.md — Secrets](../../CONTRIBUTING.md#secrets)
