# ADR-0020 — A product is a style; a variant is what you actually buy

**Status:** Accepted · **Date:** 2026-08-01 · **Supersedes:** nothing

---

## Context

Until now `Product` and *sellable unit* were the same thing. One row, one SKU, one stock figure, one price.
That is enough to demonstrate browse-and-buy, and it is what the first nine phases needed.

It cannot express a t-shirt.

A t-shirt is one product with a name, a description, a price and a photograph — and eight different things
you can put in a basket, because it comes in four sizes and two colours, and the warehouse has three of the
medium navy and none of the small. A shop that cannot say *"only 2 left in Medium"* is not modelling
clothing; it is modelling a mug.

Three requirements arrived together:

1. Clothing needs a **size** to select before it can be bought.
2. Clothing is **men's or women's**, and a customer wants to browse one or the other.
3. Everything can come in **colours**, stock is per colour-and-size, and all of it must be filterable.

---

## Decision

### 1. `ProductVariant` is a child entity of `Product`, and **the SKU moves to it**

```
Product     — the style:  name, description, price, image, category, brand, audience
ProductVariant — the unit: SKU, size, colour, stock
```

`Product.Sku` stays, and is reinterpreted as the **style code** (`NW-TS-001`). Every variant derives its own
SKU from it (`NW-TS-001-M-NAV`). A product with no size and no colour axis still has exactly one variant —
there is no special case for "simple" products, because a special case is a second code path that only the
simple products test.

**Why the SKU is the thing that moves.** SKU is already the integration key between Catalog, Inventory,
Basket and Ordering — it is the one string that crosses those boundaries. Inventory keys `StockItem` on a
SKU string. Ordering snapshots a SKU onto an order line. Basket carries a SKU.

So moving the SKU down to the variant means **every downstream service is already correct**. Inventory does
not learn what a size is; it gets more rows. Ordering does not learn what a colour is; the SKU it records
just happens to identify one. That is not luck — it is the payoff for having drawn the context boundary at
the SKU in the first place, and it is the single strongest argument in this document that the boundaries
were right.

### 2. Audience is an **attribute**, not a branch of the category tree

`Product.Audience` is `Men`, `Women` or `Unisex`.

The obvious alternative is to restructure the taxonomy — `Men > T-shirts`, `Women > T-shirts` — which is
what the navigation of most clothing retailers *looks* like. It is not what they store. A URL like
`/men-tshirts` is an **audience × category** facet combination, not a node in a tree, and modelling it as a
tree means every category exists two or three times, a product cannot be unisex without being duplicated,
and adding "Kids" doubles the tree again.

The taxonomy answers *what is this thing*. Audience answers *who is it for*. They are independent, so they
are two fields.

### 3. Stock is per variant, and the product-level figure is a **sum**

`Product.StockOnHand` becomes derived — the total across active variants — and stays exactly what it always
was: a cached, eventually-consistent display figure that Inventory owns the truth of (ADR unchanged, see
`docs/services/catalog.md`). A product card says "In stock"; a product page says "Only 2 left" **for the
size and colour you selected**, because that is the number that decides whether you can buy.

### 4. Basket and order lines snapshot size and colour as **text**

They already snapshot the product name and unit price rather than joining to Catalog, for the reason in
`docs/architecture.md §6`: an order records what was bought, not a pointer to what that thing is called
today. Size and colour are the same kind of fact. `"Medium"` and `"Navy"` are copied onto the line; renaming
a colour next year does not rewrite last year's dispatch note.

---

## What this costs

**Every product page becomes a state machine.** "Which variant is selected" is now UI state that gates the
Add to basket button, and it has to handle the combination that does not exist (Medium exists, Navy exists,
Medium-Navy does not). That is a real increase in front-end complexity, doubled by the lockstep rule
([ADR-0014](0014-react-and-angular-in-lockstep.md)).

**The seed data grows about fourfold**, and stock has to be spread across variants deliberately enough that
in-stock, low-stock, out-of-stock and *partially* out-of-stock are all reachable without editing the
database.

**A one-variant product carries ceremony it does not need.** A leather portfolio has no size and no colour,
and it still has a variant row, a variant SKU and a variant selector that renders nothing. This is accepted
deliberately: the alternative is a nullable relationship and two rendering paths, and the simple path is the
one that would rot.

**The unique index on `Product.Sku` no longer means what it used to.** It now guarantees unique *style*
codes; the constraint that matters commercially — unique sellable SKUs — moves to `product_variants.sku`.
Both indexes exist. Anyone reading only one of them will draw the wrong conclusion, which is why this
paragraph is here.

**Migration is not free for existing orders.** Orders placed before this change reference style codes like
`NW-TS-001`, which are no longer sellable SKUs. Those orders remain readable — the line snapshots its own
name and price — but a "buy it again" feature would have to resolve a style code to a variant. Nothing
implements that yet, and this is the reason it would not be trivial.

---

## Alternatives rejected

**Options as free text on the product** (`sizes: "S,M,L"`). No per-size stock, which fails the actual
requirement. It also puts a list in a string column, which the database cannot index, constrain or count.

**A generic attribute bag** (`variant_attributes` as key/value rows, or JSONB). Genuinely more flexible —
it would take "material" or "length" without a migration. Rejected because flexibility here is a cost:
every query becomes a pivot, no column can be typed, and the UI has to render attributes it cannot
anticipate. Size and colour are the two axes this catalogue has; when a third arrives, adding a column is a
morning's work and the schema still describes the domain.

**Variants as separate products with a `parentProductId`.** Self-referencing hierarchies are seductive and
they blur the one distinction that matters: a style is not sellable and a variant is not browsable. Two
concepts modelled as one type means every query needs a flag to say which kind it is dealing with.

**Audience as a category** — argued above.
