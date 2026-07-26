# Business rules and invariants

> **Related:** [Glossary](glossary.md) · [Bounded contexts](bounded-contexts.md) ·
> [Concept map](../concept-map.md)

The rules of this domain **in plain language, before any code enforces them**. Written this way on purpose: a
rule expressed only as a C# method is a rule only a developer can check, and the person who actually knows
whether it is right is usually not that developer.

## Invariant vs rule vs policy

Three things routinely conflated, with different homes in the code:

| | Meaning | Enforced | Violating it means |
|---|---|---|---|
| **Invariant** | Must *always* be true for the object to exist at all | Inside the aggregate, in constructors and methods | A **bug** — the application layer should have prevented it. Throws `DomainException`. |
| **Business rule** | Must be true for an *operation* to be allowed | Application layer, before touching the aggregate | An expected outcome. Returns a failed `Result`. |
| **Policy** | Configurable, changes without a deployment | Configuration | Nothing — it is a setting |

Example of all three on one screen: *an order's total must equal the sum of its lines* is an invariant.
*A shipped order cannot be cancelled* is a business rule. *Orders over £5,000 are declined by the payment
simulator* is a policy.

Getting this wrong is expensive in a specific way: enforcing a business rule as an invariant means the domain
throws where it should have returned a handled failure, so ordinary user mistakes surface as 500s.

## Ordering — invariants

Enforced by the `Order` aggregate. Reaching any of these is a defect.

| # | Invariant |
|---|-----------|
| O1 | An order has at least one line. An empty order cannot exist. |
| O2 | Every line has a quantity of at least 1. |
| O3 | Every line has a non-negative unit price. |
| O4 | An order's total equals the sum of its lines' quantity × unit price. |
| O5 | All lines on an order share one currency. Mixed-currency orders are meaningless. |
| O6 | An order has a complete shipping address — an address is either whole or absent, never half-populated. |
| O7 | An order has a `BuyerId`. Anonymous orders are out of scope. |
| O8 | Status transitions follow the state machine. Nothing may set a status directly. |

## Ordering — business rules

Checked by the application layer; a violation returns a failed `Result`, not an exception.

| # | Rule | Why |
|---|------|-----|
| OR1 | Only a `Submitted` or `AwaitingStock` order may be cancelled by a customer | Once payment is taken, cancelling becomes a refund — a different operation with a different permission |
| OR2 | A `Shipped` or `Delivered` order cannot be cancelled | The goods have left |
| OR3 | Only an order that has been paid may be refunded | You cannot refund money never taken |
| OR4 | A customer may read and act on only their own orders | Resource-based authorization; see [authorization-model.md](../authorization-model.md) |
| OR5 | Prices are captured at checkout and never recalculated | An order records what the customer agreed to pay |

## Basket

| # | Rule |
|---|------|
| B1 | A basket belongs to exactly one user and holds at most one line per variant — adding an existing variant increases its quantity |
| B2 | Quantity is at least 1; setting it to 0 removes the line |
| B3 | A basket expires by TTL. Expiry is correct behaviour, not data loss |
| B4 | On a price change, the line is updated **and flagged**, never silently repriced — the customer must see that what they were about to buy has changed |
| B5 | Checkout empties the basket. Basket keeps no history |

## Inventory

| # | Rule | Why |
|---|------|-----|
| I1 | Available stock = on hand − active reservations | Reserved stock is not sellable, though it is still physically present |
| I2 | A reservation cannot exceed available stock | Overselling has a direct cost |
| I3 | Stock is **reserved, never decremented, on order** | A decrement leaks stock permanently whenever anything downstream fails |
| I4 | A reservation is confirmed on payment success and released on failure or timeout | Without the timeout, an abandoned saga holds stock forever |
| I5 | Stock never goes negative | Enforced at the database with a check constraint as well as in the domain — this is the one number worth belt and braces |

## Payment

| # | Rule |
|---|------|
| P1 | A payment is attached to exactly one order |
| P2 | An order may have several attempts; at most one succeeds |
| P3 | A refund cannot exceed the captured amount, in total across all refunds |
| P4 | Payment records are append-only. A correction is a new record, never an edit |

## Customer Profile

| # | Rule |
|---|------|
| C1 | A profile is keyed by the Keycloak `sub` and holds no credentials |
| C2 | At most one default shipping address, and at most one default billing address |
| C3 | Consent records are append-only — "when did they consent, and to what wording?" is a question regulators ask |
| C4 | Withdrawing marketing consent stops marketing messages but not transactional ones; an order confirmation is part of the contract, not marketing |

## Cross-service — the ones that need a saga

These cannot be enforced by any single aggregate, which is precisely why the saga exists.

| # | Rule | Mechanism |
|---|------|-----------|
| X1 | An order is confirmed only if stock was reserved **and** payment succeeded | Saga orchestration |
| X2 | If payment fails, reserved stock is released | Compensating action |
| X3 | If stock is unavailable, the order is cancelled and no payment is attempted | Saga branch |
| X4 | A step that times out is treated as failed and compensated | Saga timeout — without this a lost reply strands an order forever |

Each is stated as *eventually* true. There is a window in which an order is `Submitted` and stock is not yet
reserved, and the UI shows that honestly rather than pretending it is instant. See
[ADR-0011](../adr/0011-orchestration-saga.md).
