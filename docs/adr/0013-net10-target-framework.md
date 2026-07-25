# ADR-0013: Target .NET 10 (LTS)

- **Status:** Accepted
- **Date:** 2026-07-25
- **Phase:** 1

## Context

Every project in the solution needs a target framework, and changing it later means touching ~40 project
files and revalidating every dependency. It is worth deciding once, deliberately.

.NET alternates release trains: **LTS** releases are supported for three years, **STS** releases for
eighteen months. As of this writing .NET 10 is the current LTS (released November 2025), .NET 9 was STS and
is out of support, and .NET 8 is the previous LTS approaching end of life.

The developer machine had only the 9.0.101 SDK installed, so this was a real choice rather than a default.

## Options considered

### Option A — .NET 9
Already installed; no setup step. But it is an **STS release that has passed end of support**, meaning no
security patches. For a repository whose purpose is to demonstrate current best practice, building on an
unsupported runtime undercuts the premise, and an interviewer would reasonably ask about it.

### Option B — .NET 8 (previous LTS)
Still supported, still very widely deployed, and the version many enterprises are pinned to. Would require
installing an SDK anyway. Two LTS generations behind current, so it forgoes several things this project uses
directly.

### Option C — .NET 10 (current LTS)
Supported until late 2028. Matches the current EF Core 10 / ASP.NET Core 10 documentation, which matters
because a teaching repository is read alongside the docs.

## Decision

**Target `net10.0` across the solution, with C# 14**, pinned by a `global.json` so the SDK version is
explicit and reproducible rather than "whatever is installed".

The framework version is set **once** in `Directory.Build.props` and inherited by every project. No
individual `.csproj` declares a `TargetFramework` — a single-line change moves the whole solution, which is
exactly the property that makes the next upgrade cheap.

Also set centrally there, because they are decisions rather than defaults:

| Setting | Value | Why |
|---------|-------|-----|
| `Nullable` | `enable` | Nullable reference types are the largest single source of null-safety in modern C#. Escalated to *errors* in `.editorconfig` — a warning everyone ignores is not a safety feature. |
| `ImplicitUsings` | `enable` | Removes ~10 lines of noise per file. |
| `TreatWarningsAsErrors` | `true` | Warnings accumulate into background noise; making them fail keeps the signal. |
| `EnforceCodeStyleInBuild` | `true` | `.editorconfig` style rules are checked at build, not only in the IDE. |
| `ContinuousIntegrationBuild` | `true` in CI | Deterministic builds and normalised paths in symbols. |

Package versions are managed by **Central Package Management** (`Directory.Packages.props`), so a package
has exactly one version across the entire solution. Without it, forty projects drift into version conflicts
that only surface at runtime as assembly-binding failures — and diamond dependencies between building
blocks and services make that near-certain.

### Installation note

The SDK was installed **per-user** (`%LOCALAPPDATA%\Microsoft\dotnet`) via the official `dotnet-install`
script, because the session was not elevated. A machine-wide install
(`winget install Microsoft.DotNet.SDK.10`, from an elevated terminal) is preferable for day-to-day use.
Documented in [getting-started.md](../getting-started.md).

Containers are unaffected — they build from `mcr.microsoft.com/dotnet/sdk:10.0` and run on
`aspnet:10.0`, so the local SDK matters only for development outside Docker.

## Consequences

### What this buys us
- Security patches until November 2028.
- Current C# language features, and code that matches the documentation a reader will have open.
- One place to change the framework version for the whole solution.
- One version per package, solution-wide, enforced by the build.

### What this costs us
- **An SDK install** was required — the friction that made this a decision at all.
- **Enterprise mismatch.** Organisations pinned to .NET 8 cannot run this without retargeting. Mitigated by
  the single-line `Directory.Build.props` change; nothing in the code depends on .NET 10-only APIs in a way
  that would resist a downgrade.
- **`TreatWarningsAsErrors` will occasionally be obstructive** — a deprecation warning from a transitive
  dependency can block a build. Handled by suppressing specific diagnostics in `.editorconfig` with a
  comment explaining why, never by disabling the setting.
- **Nullable-as-error is strict**, and integrating a library with poor nullable annotations will require
  explicit suppressions.

### What we will have to revisit
November 2028, or sooner if a target environment pins an older runtime. The upgrade is a one-line change
plus a dependency sweep, which is the intended outcome of centralising it.

## References

- [.NET support policy](https://dotnet.microsoft.com/platform/support/policy/dotnet-core)
- [Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management)
- `Directory.Build.props`, `Directory.Packages.props`, `global.json`
