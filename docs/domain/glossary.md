# Ubiquitous language

> **Related:** [Bounded contexts](bounded-contexts.md) · [Business rules](business-rules.md)

The vocabulary of this domain, defined precisely. The point of a *ubiquitous* language is that the same word
means the same thing in conversation, in documentation, and in code - so if a term here disagrees with a class
name, one of them is a bug.

**Terms that mean different things in different contexts are marked ⚠️ and defined per context.** Those are
not sloppiness; they are exactly where the bounded-context boundaries fall, and flattening them into one
definition is how you end up with a bloated canonical model that serves nobody.

---

## Identity and people

### ⚠️ Customer

| Context | Meaning |
|---------|---------|
| **Identity & Access** (Keycloak) | A set of credentials, an opaque `sub`, and a list of realm roles. Has no name, address, or preferences. |
| **Customer Profile** | A display name, contact details, saved addresses, preferences, wishlists, and consent records - keyed by `sub`. Never authenticates anyone. |
| **Ordering** | A `BuyerId` and a shipping address **frozen at the moment of purchase**. Not a live reference to a person. |

**There is deliberately no canonical `Customer` class.** Three models, each complete for its own purpose,
joined only by `sub`. See [bounded-contexts.md](bounded-contexts.md).

### Subject (`sub`)

The opaque, immutable, never-reused identifier Keycloak assigns to a user. **The only join key between
identity data and business data.** Never email or username: both are mutable and reassignable, and keying
business data on a mutable identifier guarantees an eventual orphaning incident.

### Realm role

A coarse identity in Keycloak - `customer`, `support-agent`, `catalog-manager`, `order-manager`, `admin`.
Answers *what kind of user is this*, not *what may they do*.

### Permission

A fine-grained capability - `catalog:write`, `order:refund`, `user:manage`, `price:override`. Answers
*what may they do*. Endpoints are guarded by permissions, never by roles.

### Composite role

A Keycloak role that grants other roles. This is how a realm role acquires its permissions, so the **token
carries permissions rather than job titles**. Changing what `support-agent` may do becomes a configuration
change in Keycloak instead of a code change across every endpoint.

### Staff

Any user with a realm role other than `customer`. Not a domain entity - a convenient collective noun.

---

## Catalog

### Product

A **style**, as merchandising understands it: name, description, image, category, brand, list price, and who
it is for. Owned by Catalog. **A product is not sellable** - a *variant* is. **Ordering does not hold a
Product**; it holds an `OrderItem` carrying a copy of the name and price at the time of ordering.

### Variant

A specific purchasable configuration of a product - a size and a colour. **This is what goes in a basket,
what an order line records, and what stock is held against.** Every product has at least one, including
those with neither a size nor a colour ([ADR-0020](../adr/0020-product-variants.md)).

### SKU

*Stock Keeping Unit.* The identifier of a **variant**, and the one string Catalog, Inventory, Basket and
Ordering all agree on - which is what makes it the integration key between them, and why moving it from the
product to the variant cost Inventory no schema change.

### Style code

`Product.Sku` - the identifier of the style rather than of anything sellable, e.g. `NW-TS-001`. Variant
SKUs are conventionally derived from it (`NW-TS-001-M-NAV`), but nothing parses that: the convention is a
courtesy to whoever reads a picking list.

> Both `products.sku` and `product_variants.sku` are uniquely indexed and they guarantee **different
> things** - unique styles, and unique sellable units. Reading only one leads to the wrong conclusion.

### Audience

Who a product is made for: `Men`, `Women` or `Unisex`. An **attribute, not a branch of the category
tree** - a taxonomy answers *what is this thing*, audience answers *who is it for*, and they vary
independently.

### Size scale

The set of sizes a category is sold in, **and their order**: "UK clothing" is S, M, L, XL. Stored as data
with an explicit position, because that order is neither alphabetical nor numeric. A category with no scale
is **not sized** - which is a different claim from being sold in one size
([ADR-0021](../adr/0021-category-defined-options-and-contextual-facets.md)).

### List price

The price Catalog advertises. **Not necessarily the price paid** - see *unit price*. Held on the product,
not per variant: a Medium and a Large of one shirt cost the same.

### Category / Brand

Taxonomy for browsing and filtering. Categories nest one level; brands do not. A category names the *size
scale* its products use, and a child inherits its parent's.

### Facet

A filterable axis with counts - audience, size, colour. Computed **against the current filter**, so the
options offered are the ones that would return something: choosing Stationery leaves the size facet empty
and the control disappears rather than offering a size no notebook has.

---

## Basket

### Basket

The current cart for one user. Short-lived, disposable, expires by TTL. Holds the price captured when each
line was added, not a live lookup.

### Basket line

One **variant** and a quantity, plus the captured price. Identified by its variant SKU rather than by the
product, so a Medium and a Large of one shirt are two lines rather than a merged instruction to pick "two"
of something unspecified.

### Captured price

The price recorded when an item was added. If Catalog later publishes a price change, Basket updates the line
and **flags it** so the UI can say the price changed - rather than silently altering what the customer thought
they were buying.

### Checkout

The act of converting a basket into an order. The basket publishes `BasketCheckedOut` and is then **deleted** -
Basket keeps no history.

---

## Ordering

### Order

The aggregate root. What a customer committed to buy, at what price, to which address. Immutable in its
essentials once submitted; its *status* moves through the state machine.

### Order item

An entity inside the `Order` aggregate. Carries the SKU, a **snapshot** of the product name and unit price,
and a quantity. Cannot exist independently of its order.

### Unit price

The price per unit **the customer actually agreed to pay**, frozen on the order item. Deliberately distinct
from Catalog's list price: an order records history, and a price change next week must not retroactively alter
last week's order.

### Money

A value object: an amount plus a currency. Not a `decimal`. Adding two `Money` values in different currencies
is refused by the type rather than silently producing nonsense.

### Address

A value object copied onto the order at purchase time, not a reference to the customer's saved address. If the
customer later edits that saved address, historic orders must still show where they were actually shipped.

### Order status

Where the order sits in its lifecycle: `Submitted` → `AwaitingStock` → `AwaitingPayment` → `Paid` →
`Shipped` → `Delivered`, or `Cancelled`. Transitions are enforced by the aggregate - see the
[state machine](../diagrams/README.md).

### Buyer

The `BuyerId` on an order - the Keycloak `sub` of whoever placed it. **Outside the aggregate boundary**,
referenced by id only.

---

## Fulfilment

### Saga / process manager

The component owning the sequence *order → reserve stock → take payment → confirm*, its timeouts, and its
compensations. Holds **process state, never domain state**.

### Compensating action

The business operation that semantically undoes a completed step. **Not a rollback** - you cannot un-charge a
card, you refund it; you cannot un-send an email, you send a correction.

### Reservation

A hold placed on stock for a specific order, before payment. Confirmed on payment success, released on
failure. **Stock is reserved, never decremented on order** - a naive decrement leaks stock permanently
whenever anything downstream fails.

### Stock on hand

Physical units in the warehouse. Distinct from *available* stock, which is on hand minus active reservations.

---

## Payments

### Payment attempt

One try at taking money for an order. An order may have several - a declined card followed by a successful
retry.

### Authorisation / Capture

Authorisation reserves funds on the card; capture actually takes them. **Simplified in this repository** -
the simulated gateway treats them as one step. Production separates them, typically authorising at checkout
and capturing at dispatch.

### Refund

Returning captured money. A distinct business operation with its own permission (`order:refund`) and its own
audit trail - never a deletion of the payment record.

---

## Notifications

### Channel

How a notification reaches a user: email, push, SMS. Which channels a user accepts is **profile data**, owned
by Customer Profile, not by Notification.

### Marketing opt-in

Explicit consent to non-transactional messages. Legally distinct from transactional notifications (an order
confirmation), which are sent regardless because they are part of the contract.

### Consent record

An append-only record of what a user agreed to and when. Append-only because "when did they consent, and to
what wording?" is a question regulators actually ask.

---

## Cross-cutting

### Integration event

A fact published across service boundaries, after commit, via the outbox. Past tense. A published contract -
once someone consumes it, changing it breaks their deployment.

### Domain event

Something significant that happened *inside* one service, handled in the same transaction. May reference
domain types. Never leaves the process.

### Outbox

A table holding events written in the same transaction as the business data, drained by a dispatcher. What
makes "the event is published if and only if the data was saved" true.

### Idempotent

Safe to do twice. Mandatory for every integration event handler, because delivery is at-least-once.

### Correlation ID

One identifier following a user action across every service and asynchronous hop. Quotable by a human;
survives trace sampling.

### Audit log

The append-only record of administrative actions - who did what, to what, when, from where. Owned by
Back-office. A compliance artefact, not plumbing.
