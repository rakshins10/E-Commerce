# Contributing

This repository is built in **phases**, and the commit history is itself a deliverable — it should read as a
coherent narrative of how the system was assembled. These conventions exist to keep it that way.

---

## Branching

One branch per phase, named `phase/NN-short-name`:

```
phase/01-skeleton
phase/02-keycloak-realm
phase/03-catalog-storefront
...
```

* Branch from an up-to-date `main`.
* Open a pull request into `main` at the end of the phase. The PR description states **what was built**,
  **which architectural concepts it demonstrates**, and **what to review**.
* Merge only after review.
* **`main` must always be a state where `docker compose up` works.** This is the single hard rule. If a
  phase leaves the system unbootable, the phase is not done.

Hotfixes outside the phase cadence use `fix/short-name`; documentation-only work uses `docs/short-name`.

---

## Commits

[Conventional Commits](https://www.conventionalcommits.org/), with the service or area as the scope:

```
feat(ordering): add Order aggregate root with invariants
fix(basket): make the price-changed consumer idempotent
docs(adr): record saga orchestration decision
test(catalog): add Testcontainers integration test for the product repository
chore(deploy): pin the Keycloak image to 26.4
refactor(eventbus): extract IIntegrationEventHandler from the RabbitMQ client
build(ci): run dotnet format --verify-no-changes on PRs
```

Allowed types: `feat`, `fix`, `docs`, `test`, `refactor`, `perf`, `build`, `ci`, `chore`, `style`, `revert`.

Common scopes: the service name (`ordering`, `catalog`, `basket`, `payment`, `inventory`, `notification`,
`user-profile`, `back-office`, `saga`), the gateway (`storefront-bff`, `admin-bff`, `mobile-bff`), a
building block (`eventbus`, `common`, `observability`, `auth`), a client (`react-store`, `angular-admin`,
`rn-store`, `design-tokens`), or infrastructure (`deploy`, `ci`, `keycloak`, `adr`).

### Granularity

**Commit in small, meaningful increments. Do not squash a phase into one giant commit.** A reviewer should
be able to read a commit's diff in one sitting and understand a single decision. A good rule of thumb: if
the commit message needs the word "and", it is probably two commits.

**Push after every meaningful unit of work**, not only at phase end.

---

## Secrets

**Never commit real secrets** — no `.env`, no certificates, no connection strings pointing at anything real,
no Keycloak admin credentials for a non-throwaway instance.

* `deploy/.env.example` holds dev-only placeholder values and is committed. `deploy/.env` is git-ignored.
* `identity/keycloak/realm-export.json` contains development seed credentials only. They are documented as
  dev-only in the root README and are worthless outside a local container.
* Local secrets during development go in .NET user-secrets (`dotnet user-secrets set ...`), never in
  `appsettings.json`.
* The `.gitignore` blocks `*.pfx`, `*.pem`, `*.key`, `.env`, and `secrets.json`. If you find yourself
  adding a `-f` to force a commit past it, stop.

---

## Code style

Formatting and analyzer severities are defined once in [`.editorconfig`](.editorconfig) and enforced in CI
by `dotnet format --verify-no-changes`. Run it locally before pushing:

```bash
dotnet format --verify-no-changes    # .NET
npm run lint                         # inside each /web/* app
```

Line endings are normalised to LF by [`.gitattributes`](.gitattributes) — this matters because shell
scripts and entrypoints are copied into Linux containers.

---

## Two rules that block a merge

### 1. Documentation ships with the code

**A pull request containing code but no corresponding documentation update is incomplete.** This is
docs-as-code: Markdown lives beside the source, diagrams are Mermaid committed as text (never binary images
or links to an external drawing tool), and both are reviewed in the same diff.

Concretely, in the same PR as the code:

| If the PR adds… | It must also add/update… |
|-----------------|--------------------------|
| a backend service | `docs/services/<service>.md` including the full endpoint reference |
| an endpoint | that service's endpoint reference — method, route, auth + required permission, request/response shapes, validation, status codes, error contract, idempotency, `curl` example |
| an integration event | `docs/events/event-catalogue.md` and the event-flow diagram |
| a screen | `docs/frontend/<app>/<screen>.md` and the matching `/web/ui-spec` entry |
| a domain concept | the glossary and the relevant `docs/domain/` page |
| a design decision | a new ADR, plus every diagram the decision invalidates |

Stale documentation is a bug. If a change invalidates a diagram, **update the diagram in the same commit**.
The PR description must state which docs were added or changed.

### 2. React and Angular move in lockstep

**Never build a feature in React and port it to Angular later.** Every UI feature lands in both frameworks
in the same phase and the same pull request. The sequence for each feature slice is:

1. **Specify once** — write `/web/ui-spec/<feature>.md` first: routes, states, components, validation,
   empty/loading/error behaviour, and the permissions gating it. Framework-agnostic; both implementations
   must satisfy it.
2. **Share once** — design tokens, the generated API client, OIDC config, permission helpers, formatters,
   and validation schemas live in `/web/shared` as framework-neutral TypeScript. React, Angular, and React
   Native all consume it. Never duplicate this logic per framework.
3. **Implement both, idiomatically** — use each framework properly. Do not write "Angular that looks like
   React", and do not flatten to a lowest common denominator. Demonstrating fluency in both is the point.
4. **Prove parity** — update [`web/parity-checklist.md`](web/parity-checklist.md), and make the shared
   Playwright specs pass against **both** apps (the base URL is parameterised so CI runs the suite twice).
   Attach side-by-side screenshots to the PR.
5. **Report the divergences** — add a note to [`docs/react-vs-angular.md`](docs/react-vs-angular.md) on where
   each framework was cleaner and what each forced you to work around.

If you run short of room, **finish the current feature in both frameworks rather than starting the next
feature in one.**

---

## Documenting decisions

**Any non-obvious choice gets an ADR.** Copy [`docs/adr/0000-template.md`](docs/adr/0000-template.md), number
it sequentially, and keep it short — context, the options considered, the decision, and the consequences you
accepted. An ADR is immutable once merged: if a later phase reverses it, write a new ADR that supersedes it
rather than editing history.

Every architectural pattern also gets an entry in [`docs/concept-map.md`](docs/concept-map.md) explaining
what it is, why it is here, and the interview question it answers.

---

## Pull request checklist

- [ ] `docker compose up` brings the whole system up from a clean clone
- [ ] `dotnet build` and `dotnet test` pass
- [ ] `dotnet format --verify-no-changes` is clean
- [ ] Each frontend touched lints and builds
- [ ] New decisions have ADRs; new patterns have concept-map entries
- [ ] README tables (endpoints, seed users, concept map) updated for anything the phase added
- [ ] No secrets, no `.env`, no certificates in the diff
