# ADR-0001: Record architecture decisions in this repo

- **Status:** Accepted
- **Date:** 2026-07-25
- **Phase:** 1

## Context

This repository exists to be *explained*. Its purpose is to demonstrate architectural patterns and to let
the author defend them under questioning, which means the reasoning behind each choice is as much a
deliverable as the code implementing it.

Reasoning decays fast. Six months after a decision, the code shows *what* was done but nothing shows *what
else was on the table* or *what constraint forced the outcome*. The usual places this knowledge goes —
pull-request threads, chat, a wiki nobody updates — are either unsearchable, outside the repo, or drift out
of sync with the code they describe.

There is a second, sharper failure mode: without a record, a future reader cannot distinguish a **considered
decision** from an **accident**. That ambiguity is expensive, because it makes people afraid to change
things that were arbitrary and cavalier about changing things that were load-bearing.

## Options considered

### Option A — Comments in the code
Cheap and adjacent to the thing being explained. But comments explain a *file*; architectural decisions
span many files or exist between them (there is no single file where "we chose orchestration over
choreography" lives). They also bloat source with prose that most readers do not want inline.

### Option B — A wiki or external document
Rich formatting, easy for non-developers to reach. But it lives outside version control, so it cannot be
reviewed in a pull request, cannot be diffed against the change that motivated it, and reliably drifts —
the code moves, the wiki does not.

### Option C — Architecture Decision Records in the repo
Short Markdown files, numbered sequentially, committed alongside the code, reviewed in the same pull
request as the change they justify. The format popularised by Michael Nygard.

## Decision

**Use ADRs, stored in `docs/adr/`, one file per decision, numbered sequentially and never renumbered.**

Each record states the context that forced the decision, the options genuinely considered, the choice, and
the consequences accepted — including the costs. Records are **immutable once merged**: a reversal is a new
ADR that supersedes the old one, and the old one stays in place.

Why immutability matters: the fact that we once believed something different is information. It tells a
future reader that the question was asked, and it prevents a decision being quietly reversed without anyone
noticing the trade-off changed. Editing an ADR to reflect current thinking destroys exactly the record it
exists to keep.

The template ([`0000-template.md`](0000-template.md)) forces two sections teams usually skip: **options
genuinely considered** (not straw men) and **what this costs us**. An ADR with no downsides is marketing,
not engineering, and an interviewer will notice.

## Consequences

### What this buys us
- The reasoning lives with the code, is reviewed with the code, and is versioned with the code.
- Onboarding shortcut: a reader can absorb the system's shape from `docs/adr/` before opening a `.cs` file.
- A direct interview artefact — each ADR is a rehearsed answer to "why did you do it that way?"
- It becomes obvious when a change contradicts a prior decision, because the contradiction has to be written
  down to be merged.

### What this costs us
- Writing discipline. An ADR takes 20–40 minutes to write honestly, and the temptation under time pressure
  is to skip it — which is exactly when the reasoning is most worth capturing.
- A real risk of ADRs becoming *aspirational*: describing what we meant to do rather than what the code
  does. Mitigated by requiring the ADR in the same PR as the code, so a reviewer sees both.
- Sequential numbering means merge conflicts on the number when two branches add ADRs concurrently. Cheap to
  resolve; noted so it is not a surprise.

### What we will have to revisit
If this repository ever became a real product with a team, ADR volume would grow past the point where a flat
directory and a hand-maintained index work. At that point: group by area, or adopt tooling
(`adr-tools`, Log4brains). Not worth it at this scale.

## References

- Michael Nygard, [*Documenting Architecture Decisions*](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions)
- [`0000-template.md`](0000-template.md) — the template
- [`CONTRIBUTING.md`](../../CONTRIBUTING.md) — the rule that documentation ships with the code
