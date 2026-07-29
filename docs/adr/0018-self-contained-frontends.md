# ADR-0018: Each frontend owns its code, even where that duplicates

- **Status:** Accepted
- **Date:** 2026-07-26
- **Phase:** 3
- **Amends:** [ADR-0014](0014-react-and-angular-in-lockstep.md) — the lockstep requirement stands; the
  shared-layer *mechanism* in step 2 is replaced by what follows.

## Context

[ADR-0014](0014-react-and-angular-in-lockstep.md) put everything framework-neutral — permission helpers,
OIDC configuration, formatters, the API client, design tokens — into a single `@ecommerce/shared` package
consumed by both storefronts. That is the conventional engineering answer, and it worked: a one-line fix to
the OIDC scope corrected both apps at once.

But this repository's **primary purpose is not shipping software.** It is a study and interview artefact for
one engineer preparing for senior and architect roles across both ecosystems. That changes what "good"
means, and the shared layer worked against it in a specific way:

**To understand how authentication works in the React app, you had to read code in three places** — the
component, the hook, and a sibling package outside the application entirely. The same for Angular. Neither
application could be read, understood, or explained on its own. For a codebase whose job is to be *studied*,
and to be walked through in an interview, indirection across a package boundary is a real cost paid on every
reading.

There is also a demonstration argument. "I built this storefront in React" is a weaker claim when a third of
the interesting logic lives somewhere neither implementation owns.

## Options considered

### Option A — Keep `@ecommerce/shared` (the status quo)
Correct by normal engineering standards. One implementation of permission parsing, so React and Angular
cannot disagree about what a token means. A bug is fixed once.

Rejected because it optimises for a maintenance property this repository does not need at the expense of the
comprehension property it exists to provide.

### Option B — Duplicate everything, no shared layer at all
Each application contains every line it needs. Reading `web/react-store` tells you the whole story.

The cost is real and must not be minimised: the same logic exists twice, a bug must be fixed twice, and
nothing *structurally* stops the two drifting apart.

### Option C — Share only the design tokens, duplicate the logic
A middle position: colours and spacing stay shared so the apps cannot look different, while behaviour is
duplicated.

Rejected as the worst of both. It still forces the reader outside the application, and for the *one* thing
where drift is immediately visible on screen — and therefore the easiest to catch without tooling.

## Decision

**Option B. Each frontend is self-contained.** Nothing in `web/react-store` imports from outside itself, and
likewise for `web/angular-store`.

| Concern | React | Angular |
|---------|-------|---------|
| Permissions | `src/lib/permissions.ts` | `src/app/core/permissions.ts` |
| OIDC config, token parsing | `src/lib/auth.ts` | `src/app/core/auth-config.ts` |
| Formatters | `src/lib/formatting.ts` | `src/app/core/formatting.ts` |
| API client | `src/lib/api-client.ts` | `src/app/core/api-client.ts` |
| Design tokens | `src/styles/tokens.css` | `src/styles/tokens.css` |

**The lockstep requirement from ADR-0014 is unchanged and now matters more.** Both frameworks are still
built in the same pull request, and the same Playwright suite still runs against both.

### How drift is caught, now that structure no longer prevents it

This is the part that makes the decision defensible rather than merely convenient. Three mechanisms:

1. **The shared e2e suite** ([`tests/e2e`](../../tests/e2e/)) runs identical specs against both apps. It
   asserts on visible text and accessible names, so a divergence in permission parsing, formatting, or
   labelling fails CI. This is the primary guard, and it tests *behaviour* — which is what actually matters —
   rather than testing that two files are identical.
2. **A token-parity check** ([`scripts/check-design-tokens.mjs`](../../scripts/check-design-tokens.mjs))
   validates both apps' `tokens.css` against WCAG 2.2 AA contrast **and** asserts they are identical. Visual
   drift is the least likely to be noticed by a test and the most likely to be noticed by a user.
3. **The parity checklist** ([`web/parity-checklist.md`](../../web/parity-checklist.md)) remains the human
   record.

**Behaviour is verified; structure is not enforced.** That is the trade being made deliberately.

## Consequences

### What this buys us
- **Each application is readable end to end on its own.** Open `web/react-store` and every line that makes it
  work is inside it. This is the entire point.
- Each is a standalone, portable demonstration of that framework.
- The React/Angular comparison becomes genuinely like-for-like: both implement the same thing from scratch,
  so [`docs/react-vs-angular.md`](../react-vs-angular.md) compares two real implementations rather than two
  thin wrappers over one.
- No build-order coupling, no workspace resolution to explain, no package boundary to cross while debugging.

### What this costs us
- **Every logic change is made twice.** Forget one and the apps diverge. This is the cost, stated plainly.
- **A bug is fixed twice.** The `offline_access` scope error in Phase 3 was one line in the shared package
  and corrected both apps; under this decision it would have been two edits, with a real chance of fixing
  React and leaving Angular broken.
- **Nothing structurally prevents drift** — only tests do, and only for behaviour they cover.
- More code overall, and more to review.

### What we will have to revisit
If this ever became a product with a team, reverse it: extract the duplicated logic back into a shared
package and accept the indirection. The trigger would be the first bug fixed in one app and not the other —
and if that happens more than once, the tests are not covering enough.

## References

- [ADR-0014](0014-react-and-angular-in-lockstep.md) — lockstep delivery, still in force
- [`tests/e2e`](../../tests/e2e/) — the parity proof this decision now leans on
- [`scripts/check-design-tokens.mjs`](../../scripts/check-design-tokens.mjs) — contrast and visual-drift guard
