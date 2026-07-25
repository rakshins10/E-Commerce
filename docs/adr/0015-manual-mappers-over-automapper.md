# ADR-0015: Hand-written mappers instead of AutoMapper

- **Status:** Accepted
- **Date:** 2026-07-25
- **Phase:** 1

## Context

The system maps between representations constantly: domain entity → DTO, DTO → command, gRPC message →
domain type, Keycloak's `UserRepresentation` → our user DTO. Some of these are near-identical shapes; others
are genuine translations across a context boundary.

The brief explicitly asks for AutoMapper **or** manual mappers, with a justification — so this is a decision
that must be argued rather than defaulted.

There is also a fact that changes the calculus: **AutoMapper moved to a commercial licence in 2025.** Free
below a revenue threshold, paid above it. That is not disqualifying, but it means adopting it is now a
procurement decision as well as a technical one, and an interviewer may well ask whether you knew.

## Options considered

### Option A — AutoMapper
Convention-based mapping. Configure profiles once; matching property names map automatically.

Real strengths: for wide, boring, near-identical DTOs it removes a lot of typing, and
`ProjectTo<T>()` composes with `IQueryable` so EF Core can translate the projection into SQL that selects
only the needed columns — genuinely valuable and the strongest argument in its favour.

Costs:
- **Failures are at runtime, not compile time.** Rename a property and the mapping silently produces a
  default value. A `null` where a name should be, discovered in production. Mitigated by
  `AssertConfigurationIsValid()` in a test — which is essentially rebuilding compile-time safety by hand.
- **Invisible behaviour.** `_mapper.Map<OrderDto>(order)` is unnavigable; "where does this field come from?"
  requires reading profiles elsewhere.
- **Convention magic hides mistakes.** Two same-named properties meaning different things map happily.
- **Debugging is genuinely unpleasant** once custom resolvers, conditions, and nested profiles accumulate.
- **Licensing.**

### Option B — Hand-written mapping (extension methods / static `ToDto()`)
Explicit, ordinary C#.

### Option C — A source generator (Mapperly, Riok)
Generates the mapping code at compile time from a partial method declaration. Compile-time safe, zero
runtime reflection, generated code is inspectable, and unmapped members are a build warning.

**Objectively the strongest engineering option**, and what a production system should probably use. Rejected
*here* for a specific reason: the generated code is what a reader most wants to see, and hiding
`OrderItem → OrderItemDto` behind a generator removes exactly the boundary-translation code that this
repository exists to make explicit.

## Decision

**Hand-written mappers**, as extension methods living next to the DTO they produce.

```csharp
// Ordering.Application/Orders/Mappings/OrderMappings.cs
internal static class OrderMappings
{
    public static OrderSummaryDto ToSummaryDto(this Order order) => new(
        order.Id,
        order.Status.Name,
        order.OrderDate,
        order.GetTotal().Amount,
        order.GetTotal().Currency,
        order.OrderItems.Count);
}
```

The reasoning, in order of weight:

1. **Compile-time safety.** Rename a property and the build breaks — at the mapping site, with the correct
   line number. This matters far more in a system where DTOs cross service boundaries and a silently-null
   field becomes another service's bug.
2. **Mapping across a bounded context is not mechanical.** Ordering's `OrderItem` is not a projection of
   Catalog's `Product`; it is a *different concept* that happens to share a name and a price. That is the
   anticorruption layer ([bounded-contexts.md](../domain/bounded-contexts.md#the-relationship-patterns-used-and-why)),
   and convention-based mapping actively encourages treating it as a shape change — which is how a foreign
   model leaks inward. **Where a mapper is trivial enough for AutoMapper to be worth it, the boundary is
   probably in the wrong place.**
3. **Explicitness is the deliverable.** A reader can see exactly how a domain object becomes a wire
   contract, in one file, without learning a configuration DSL.
4. **No dependency, no licence, no runtime reflection.** Hand-written mapping is also measurably the fastest
   option.

### Where the cost is felt, honestly

The read side does **not** need mappers at all — Dapper projects SQL directly into DTOs
([ADR-0012](0012-cqrs-with-mediatr.md)), which removes the single largest category of mapping code and
takes most of the tedium with it. Where mapping does remain (Back-office's translation of Keycloak
representations, gRPC message conversion), it is boundary translation with real logic, exactly the code that
should not be automated away.

## Consequences

### What this buys us
- Renames and removals break the build instead of production.
- Every translation is navigable with "go to definition".
- Anticorruption layers are visible as code rather than implied by configuration.
- No package, no licence, no reflection, no startup configuration validation.
- Mapping code is trivially unit-testable with no framework.

### What this costs us
- **Boilerplate.** Wide DTOs are tedious to write, and tedium invites mistakes of a different kind —
  copy-paste errors that compile fine. Partially offset by the read side needing none.
- **Losing `ProjectTo`.** AutoMapper's `IQueryable` projection is a genuine loss on the EF Core side. Not
  felt here because reads go through Dapper, where the SQL already selects only what is needed — but in a
  system without that split it would be a strong counter-argument.
- **Manual maintenance.** Adding a property to a DTO means remembering to map it. There is no
  "unmapped member" warning as Mapperly would provide.
- **It does not scale to hundreds of DTOs.** At that volume the tedium wins and Option C becomes correct.

### What we will have to revisit
If mapping code becomes a meaningful share of the codebase, adopt **Mapperly** (Option C) rather than
AutoMapper — it keeps compile-time safety while removing boilerplate, and the migration is incremental
because the call sites (`order.ToSummaryDto()`) can stay identical.

## References

- [ADR-0012](0012-cqrs-with-mediatr.md) — why the read side needs no mapping at all
- [domain/bounded-contexts.md](../domain/bounded-contexts.md) — anticorruption layers as real translation
- [Mapperly](https://mapperly.riok.app/) — the option to reach for if this decision stops holding
