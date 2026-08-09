# ADR-0021 - The category defines which options exist; facets follow the current filter

**Status:** Accepted · **Date:** 2026-08-09
**Revisits:** two alternatives rejected in [ADR-0020](0020-product-variants.md)

---

## Context

[ADR-0020](0020-product-variants.md) gave products variants with a size and a colour, and it made two
calls that this ADR reverses. Both were defensible at the time and both are now wrong, for the same
reason: the catalogue got a second kind of product and the assumptions stopped holding.

### What went wrong, concretely

Choose **Stationery** in the category filter. The Size control still offers **M (6)**. Choose it and the
page says **0 products** - because no notebook has ever had a size, and the shop just walked the customer
into a dead end it could have known about.

The Size options came from `GET /api/catalog/facets`, which took no arguments and answered *"every size
anywhere in the catalogue"*. That is the right answer to a question nobody asked. A filter panel is not
an inventory of what exists; it is a list of what would do something.

### And the size scale is a literal in SQL

```sql
ORDER BY array_position(ARRAY['S','M','L','XL'], v.size)
```

That appears twice. It says, in the database layer, that this shop sells clothing. Adding shoes (`40`,
`41`, `42`) or bottles (`350ml`, `500ml`) means editing SQL in two places and getting the ordering wrong
in a third - and a merchandiser adding a category cannot do any of it.

---

## Decision

### 1. A size scale is data, and a category names the one it uses

```
size_scales                     size_scale_values          categories
  id                              id                         ...
  name  "UK clothing"             size_scale_id              size_scale_id  (nullable)
  slug  "uk-clothing"             value    "M"
                                  position  1
```

A category with **no** `size_scale_id` is not sized. That is Stationery and Drinkware today, and it is why
a notebook has no size rather than a size of "One size" - which would be a different and wronger claim.

`position` gives the scale its order, so `S, M, L, XL` sorts correctly *because the data says so* rather
than because a SQL literal agrees. `array_position(ARRAY[...])` is gone; the ordering joins the scale.

**A child category inherits its parent's scale when it has none of its own.** T-shirts and Hoodies do not
each need to declare "UK clothing"; Clothing declares it once. A child may still override - shoes under
Clothing would.

### 2. Facets are computed against the current filter

`GET /api/catalog/facets` now takes the same query parameters as `GET /api/catalog/products`, and returns
only values that appear in that result set, with counts to match. Under Stationery, `sizes` is empty and
the control is not rendered at all.

**ADR-0020 rejected this on cost:** "that needs a query per facet per request and is the point at which a
search index earns its keep". That reasoning was about *counts*, and it was right about counts. It was
wrong to conclude that the whole idea could wait, because the failure is not a slightly-off number - it is
an option that cannot work. The cost is also smaller than stated: three aggregates over a twelve-row table
behind one round trip, on the same `WHERE` the search already builds.

### 3. What is deliberately NOT built

**A generic attribute bag.** ADR-0020 rejected `variant_attributes` as key/value rows, and that still
stands. A variant keeps typed `size` and `colour_name` columns. What is configurable is *which values are
legal and in what order* - a schema per category, not a schema-less product.

The difference matters: adding "Material" as a third axis is a migration and a column, and the UI knows
what a material is. In an attribute bag it is a row, and the UI can only render `key: value` for something
it has never heard of. This catalogue has two axes. When it has a third, adding it is a morning's work and
the schema still describes the domain.

---

## What this costs

**Two more tables and a join on the hottest query in the system.** Product browse now joins
`size_scale_values` to order variants. It is an indexed join on a tiny table, but it is not free, and it is
on the path every visitor takes.

**Facets can no longer be cached indefinitely by the client.** They used to be "the taxonomy", fetched once
and kept for five minutes. They are now a function of the current filter, so they are refetched whenever
the filter changes - roughly doubling the requests a browse page makes. Both storefronts cache per filter
combination rather than globally, which recovers most of it, and neither app blocks rendering on facets.

**A category with the wrong scale silently produces nonsense.** Point Drinkware at the clothing scale and
the admin will offer S/M/L/XL for mugs. Nothing prevents it; the scale is merchandising configuration and
merchandising can be configured wrongly. The alternative - hard-coding which categories may be sized - is
what this ADR exists to remove.

**Inheritance is one level, matching the taxonomy.** A grandchild category would not inherit through two
levels. The taxonomy is deliberately two deep
([bounded contexts](../domain/bounded-contexts.md)), so this is consistent rather than general, and a third
level would need this revisited along with everything else that assumes two.

---

## Alternatives rejected

**Keep facets global, and grey out options that would return nothing.** Needs the same per-filter
computation to know which to grey out, so it costs what the real fix costs and still shows the customer a
control full of things they cannot have.

**Hide the Size control whenever a category is selected.** Cheap, and wrong in the other direction: under
Clothing the sizes are exactly what a shopper wants, and under T-shirts they are the *only* filter that
matters.

**Derive the scale from whatever sizes the category's products happen to have.** No configuration, and it
reads well until the first size sells out everywhere - at which point the scale silently loses a value and
the admin can no longer restock it. Configuration must not be an accident of stock.

**Put size scales in the Inventory service.** Suggested by "configurable in inventory" as a phrase, but
wrong as a boundary. Inventory owns *how many are on the shelf*. Which sizes a category is sold in is a
merchandising decision, and merchandising is Catalog. Inventory keys on a SKU string and should continue
not to know what a size is ([ADR-0020](0020-product-variants.md)).
