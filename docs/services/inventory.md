# Inventory service

> **Bounded context:** Inventory (supporting) · **Port:** 5005 · **Store:** PostgreSQL
> **Code:** [`src/services/inventory/`](../../src/services/inventory/)
> **Related:** [Ordering saga](ordering-saga.md) · [Catalog](catalog.md)

## Purpose

How much of each thing there is, how much is spoken for, and the reservation that connects the two to an
order.

---

## Three numbers, and the distinction between them is the model

| | Meaning |
|---|---|
| **On hand** | Physically on the shelf. Only goods-in or a dispatch changes it. |
| **Reserved** | Spoken for by an order that has not shipped. **Still on the shelf.** |
| **Available** | `on_hand − reserved`. What a new order may take. |

Collapsing reserved into on-hand — decrementing the count when an order is placed — is the obvious
simplification and it is wrong in a way that costs money:

- the warehouse would report fewer items than are physically present;
- a stock take would disagree with the system;
- cancelling an order could not be distinguished from a sale.

Keeping them separate means the **physical count and the commercial count are both true at once**.

**Available is derived, never stored**, for the same reason an order total is: a stored copy is a second
source of truth that drifts the first time somebody updates one number and not the other.

---

## Reservation is asynchronous, and that is a decision

The customer is **not waiting** on it. They clicked "Place order", got an order number, and are looking
at a confirmation. Whether the warehouse can fulfil it is answered afterwards.

Compare the basket, which Ordering fetches *synchronously* during checkout — there is nothing to order
without it.

> The rule of thumb: **synchronous when the caller cannot proceed without the answer; asynchronous when
> the answer changes what happens next but not whether the request succeeded.**

---

## All or nothing

A ten-line order where one item is out of stock reserves **nothing**.

Partially reserving would leave the customer charged for items they will not receive, while other
customers are blocked from buying the items now sitting reserved for an order about to be cancelled.

Enforced by the transaction: every `Reserve` happens before a single `SaveChangesAsync`, so a rejection
part way through leaves the database exactly as it was. **All-or-nothing needs no compensating action
precisely because nothing was ever committed** — `ChangeTracker.Clear()` discards the lot.

One ordering detail matters there: clear the tracker **first**, then add the rejection outbox row. The
other way round throws the rejection away along with the reservations, and the saga waits forever for an
answer that was never sent.

---

## Release is a compensating action, and it shows

```csharp
public void Release(int quantity)
{
    Reserved = Math.Max(0, Reserved - quantity);   // clamped, deliberately
}
```

**Clamped at zero on purpose.** A compensating action will be retried, possibly long after the original
succeeded. Subtracting again would push `Reserved` negative and inflate `Available` **above what
physically exists** — and the shop would cheerfully sell stock it does not have.

Idempotent at two levels: the reservation is marked released and ignored on a second visit, *and* the
clamp catches anything that gets past that.

A release for a reservation that does not exist is logged at **warning**, not swallowed. It means the
saga and Inventory disagree about what happened, which is worth someone looking at.

`Ship` is the only place `on_hand` finally falls. Until dispatch the goods are physically present and
merely spoken for — which is exactly what the reserved/on-hand split exists to express.

---

## Endpoints

| Method | Route | Permission | Purpose |
|--------|-------|-----------|---------|
| `GET` | `/api/inventory` | `inventory:read` | Stock levels, most constrained first |
| `GET` | `/api/inventory/low-stock` | `inventory:read` | At or below the reorder level |
| `POST` | `/api/inventory/{sku}/adjust` | `inventory:adjust` | Goods in, damage, or a stock take |

**Reservations are never made or released over HTTP.** They happen only in response to saga commands. An
endpoint that could reserve stock directly would let someone create a reservation no saga knows about and
nothing will ever release.

The adjustment endpoint **requires a reason**, because an unexplained stock movement is impossible to
audit — and it is logged at Information with that reason, so somebody investigating a discrepancy can
find it.

Low stock is calculated against **available**, not on-hand: stock that is spoken for cannot be sold, so a
shelf full of reserved items still needs reordering.

---

## The duplicate stock number in Catalog

Catalog keeps a cached `stock_on_hand` so the product grid can show "Only 2 left" without calling
Inventory on every page load. Inventory holds the authoritative figure.

That duplication is a considered trade — a browse page that fans out to another service on every render
is a browse page that falls over — and the seed data sets both to the same values so the demo starts
consistent.

**The gap, named rather than hidden:** in a complete implementation Catalog would subscribe to a
`StockLevelChanged` event and update its copy. That is left out because it is the same
outbox-and-consumer pattern already demonstrated three times, and a fourth copy would be repetition
rather than teaching.

---

## Events

| Consumes | Publishes |
|----------|-----------|
| `ReserveStockCommand` | `StockReservedIntegrationEvent` |
| `ReleaseStockCommand` | `StockRejectedIntegrationEvent` |
| `OrderShippedIntegrationEvent` | `StockReleasedIntegrationEvent` |

`StockRejected` names the **unavailable SKUs**, because "something in your order is out of stock" is
useless to a customer with twelve items.

`StockReleased` is published even though nothing currently consumes it: a compensating action that leaves
no trace is indistinguishable from nothing having happened.

---

## Seed data

Twelve SKUs matching the catalogue, with a spread of levels so every UI state is reachable: plenty in
stock, low stock, and out of stock.

`FB-ST-003` — the £5,200 Leather Portfolio — has stock **on purpose**: it reserves successfully and is
then declined by the payment simulator, which is how the compensation path is demonstrated from the
storefront.
