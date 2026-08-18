# ADR-0022 - Hardening defaults: rate limits at the edge, retry budgets everywhere

**Status:** Accepted · **Date:** 2026-08-17 · **Phase:** 10

---

## Context

Phase 10 is the hardening pass. By the time it started, most of what the phase plan promised already
existed, built when the feature that needed it was built: OpenTelemetry tracing with Jaeger, structured
logs with Seq, correlation ids minted by every front end and echoed by every service, health checks split
into liveness and readiness, resilience handlers on the inter-service HTTP clients, DI validation at
boot, and the saga's compensation path proven by e2e specs.

What remained were the pieces with no feature to ride in on - the ones that only matter when something
goes wrong:

1. **Nothing rate-limited anything.** Any client could send unlimited requests to the gateways.
2. **The outbox retried a failing message forever.** No errors surfaced; the only symptom was an
   `attempts` counter climbing once a second, invisibly.
3. **The saga had no timeouts.** If a service died mid-checkout, the order sat in `AwaitingPayment`
   forever with the customer's stock still reserved. The saga page said so, honestly, as a known gap.
4. **The SPA responses carried no Content-Security-Policy.**

## Decision

### Rate limiting lives at the BFFs, and only there

A fixed-window budget per client IP (default 1000 requests / 10 seconds, configuration-overridable),
rejecting with `429` and a `Retry-After` header. Health endpoints are exempt - an orchestrator polling
`/health` is load the platform generates, and throttling it makes the limiter kill its own container.

**Only at the edge.** The services behind the gateways accept traffic from the internal network only;
giving each of them its own limiter means eleven copies of a policy and a debugging session for whoever
hits an internal 429 two hops deep. The gateway is where anonymous abuse arrives, so the gateway is where
the budget is spent. Authentication is deliberately the opposite - validated at the edge AND at every
service - because a forged identity is worth defending in depth in a way a request budget is not.

**Fixed window, not sliding or token bucket.** It is the algorithm a reader can verify with a loop and a
watch: N requests pass, request N+1 gets 429, the next window starts fresh. The fancier algorithms shave
boundary effects; they do not change what a demo shows, and this repository optimises for being
understood.

**Partitioned by IP**, because that is the only identity an unauthenticated attacker has. The cost,
honestly: everyone behind one NAT shares a budget, which is why the default is generous rather than
tight, and why it is configuration rather than a constant.

### The outbox gets a retry budget

After `MaxAttempts` failures (default 25) a message is **parked**: the fetch predicate skips it, an
ERROR-level log fires once as it crosses the budget, and the row stays in the table with its `last_error`
for diagnosis. Re-queueing after a fix is one UPDATE - the runbook has it.

Parking is a predicate, not a schema change, and emphatically not a delete. At-least-once delivery means
"we never silently drop a message"; it has never meant "we retry forever". A poison message retried every
second is not reliability, it is a heater.

### The saga gets its timeout, and the refund path gets its reason to exist

A `StuckSagaSweeper` background service compensates any saga still unfinished after a threshold (default
15 minutes; dev compose sets 2 so it can be watched). The compensation is *identical* to the payment
failure path - release stock only if this saga's own record says it was reserved, cancel the order, all
through the outbox in one transaction - because a timeout is not a special kind of failure, it is a
failure whose notification never came.

The sweep creates a race the design already anticipated: payment can succeed *after* the sweep cancelled
the order. `PaymentSucceededHandler` now detects a success arriving on a compensated saga and sends
`RefundPaymentCommand` - the command that had existed unsent since Phase 7, declared then precisely so
that adding a timeout later would have a complete story. Money is never kept for an order that does not
exist.

Multiple replicas can sweep the same saga safely: both write the same terminal state and every downstream
effect is idempotent (release clamps at zero, cancelling a cancelled order is ignored). Correctness by
idempotent effects rather than leader election - one fewer thing to run.

### CSP on the pages, minimal headers on the APIs

The SPAs' nginx config gains a Content-Security-Policy: scripts locked to `'self'`, connections to the
BFFs and Keycloak only, frames to Keycloak (OIDC silent renew runs in a hidden iframe). `style-src`
keeps `'unsafe-inline'` because Angular injects component styles at runtime; the alternative is a nonce
pipeline that buys little while scripts stay locked down. The BFFs add `nosniff`, `DENY`, and
`no-referrer` - API responses need no more.

## What this costs

**A false sweep is a cancelled order that did not deserve it.** The threshold is the trade: too short and
a slow payment gets refunded instead of confirmed; too long and stock stays hostage. Fifteen minutes
against a seconds-long happy path errs far to the safe side, and the refund path bounds the damage of
erring anyway - the customer gets their money back, not a lost order that was paid for.

**A parked message is a manual decision.** The budget converts an invisible infinite retry into a visible
stopped one; somebody still has to notice the ERROR log and act. That is the point - an unbounded retry
*pretends* no decision is needed - but it is honest to say the queue does not drain itself.

**The rate limit is per-instance state.** Scale a BFF to three replicas and a client gets three budgets.
Fixing that needs a distributed limiter backed by Redis, which is real operational surface for a problem
this deployment does not have. Recorded here so the day it does, the limitation is a known one.

**Dev and prod sweep thresholds differ** (2 minutes vs 15), which is a genuine dev/prod divergence of the
kind this project usually refuses. Accepted because a 15-minute demo is a demo nobody runs, and the
*mechanism* under test is identical - only the clock differs.

## Alternatives rejected

**Rate limiting in YARP route middleware per downstream.** Per-route budgets are tuning nobody has data
for yet; a single edge budget is comprehensible and measurable first.

**A dead-letter table for parked messages.** A second table means a second thing to monitor and a copy
step that can itself fail. The message already has a home; a predicate is enough to stop the bleeding and
keeps the evidence where the diagnosis happens.

**Timeout per saga step with different budgets.** More precise, and unjustifiable without production
latency data. One overall staleness threshold answers the operational question that exists today - "is
anything stuck?" - with one number.
