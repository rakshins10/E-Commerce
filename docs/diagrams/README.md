# Diagrams

All diagrams are **Mermaid committed as text**, never binary images or links to an external drawing tool.
That is not an aesthetic preference: a PNG cannot be diffed, cannot be reviewed in a pull request, and is
never updated when the design changes. A Mermaid block is code, and it is edited **in the same commit** as
the change that invalidates it.

| Diagram | Where | Status |
|---------|-------|--------|
| C4 — system context | [`../architecture.md §2`](../architecture.md#2-c4-level-1--system-context) | ✅ |
| C4 — containers | [`../architecture.md §3`](../architecture.md#3-c4-level-2--containers) | ✅ |
| C4 — component (service anatomy) | [`../architecture.md §4`](../architecture.md#4-c4-level-3--anatomy-of-a-service) | ✅ |
| Bounded-context map | [`../domain/bounded-contexts.md`](../domain/bounded-contexts.md#the-context-map) | ✅ |
| Deployment topology | [`deployment.md`](deployment.md) | ✅ |
| Saga happy path + compensation | [`../adr/0011-orchestration-saga.md`](../adr/0011-orchestration-saga.md#the-flow) | ✅ |
| Order state machine | `order-state-machine.md` | Phase 6 |
| Ordering aggregate boundary | `ordering-aggregate.md` | Phase 6 |
| ERD per service | `erd/` | per service phase |
| Event flow and outbox path | `event-flow.md` | Phase 6 |
| Sequence — login, refresh, client credentials | `sequence-auth.md` | Phase 2 |
| Sequence — checkout through fulfilment | `sequence-checkout.md` | Phase 7 |
| Sequence — admin refund, role assignment | `sequence-admin.md` | Phase 10 |
| Frontend route maps with permission gating | `frontend-routes.md` | Phase 3 |

## Conventions

- **Direction:** top-to-bottom for structure, left-to-right for sequence.
- **Solid arrows are synchronous; dashed are asynchronous.** Applied consistently, this makes the coupling in
  a system readable at a glance — a diagram of mostly solid lines is telling you something.
- Anything simulated or external is drawn with a dashed border.
- Ports appear on deployment diagrams only; they are noise everywhere else.
- Every diagram carries a link back to the code or configuration it describes.
