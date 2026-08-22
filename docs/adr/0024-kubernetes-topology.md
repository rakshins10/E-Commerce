# ADR-0024 - Kubernetes topology: Kustomize, HAProxy, and one hostname per surface

**Status:** Accepted · **Date:** 2026-08-21 · **Phase:** 12

---

## Context

Thirty containers run under Docker Compose today. Compose is a single-machine tool: it starts containers
in dependency order on one host and stops there. It has no answer for the questions that define running
software for other people - what happens when a container dies at 3am, how a new version reaches users
without dropping their requests, how load is spread across three copies of a service.

Kubernetes answers those, and charges for the answers in vocabulary. This ADR fixes the shape of the
translation so the manifests are not a hundred arbitrary decisions.

Four choices actually matter.

## Decision 1 - Kustomize, not Helm

**Kustomize.** Plain YAML plus overlays that patch it: `kubectl apply -k deploy/k8s/overlays/local`.

Helm is the more common answer and is genuinely better at one job - distributing software to *other
people*, who need values they can set without reading your templates. That is not this. Here the
manifests are teaching material, and Helm's cost lands squarely on that: a Helm chart is Go templates
that *produce* YAML, so a reader must hold two languages at once and cannot read a manifest and know
what will be applied. `if .Values.ingress.enabled` is not Kubernetes; it is a preprocessor.

Kustomize's base manifests are real, valid, readable Kubernetes YAML. An overlay is a patch expressed in
the same language. `kubectl kustomize` renders the result so you can read exactly what is about to be
applied, and `kubectl` has it built in - there is nothing to install.

**What it costs:** no conditionals, no loops, no functions. Adding a tenth service means writing its
Deployment and Service by hand, and most of those lines will be near-identical to the last one's.
Kustomize's answer is a shared component patch, which helps but does not eliminate the repetition. Helm
would have written the tenth service from a template. Accepted: the duplication is visible, and visible
duplication in a file a reader can read beats a template they must execute in their head.

## Decision 2 - HAProxy as the ingress controller

Something has to turn one entry point into "which of these ten web surfaces did you want". Kubernetes
calls that an Ingress - and the Ingress object is only a *description*. A controller has to implement
it. The default choice is ingress-nginx.

**HAProxy Kubernetes Ingress Controller,** for one reason that dominates here: **the stats page.**

HAProxy ships a built-in HTML dashboard listing every backend, every server in it, its health-check
state, its current and total sessions, and its bytes moved - live, refreshing, with no metrics stack to
install. During a rolling update you can watch sessions drain off the old pod and appear on the new one,
in a browser, in real time. That is exactly the visibility this phase set out to provide, and it costs
one Service to expose.

ingress-nginx exposes broadly the same information as Prometheus metrics, which is the more *correct*
answer for production monitoring - and requires Prometheus and Grafana before you can see anything at
all. For learning what a load balancer is actually doing, an HTML page beats a metrics endpoint.

**What it costs:** HAProxy's ingress controller has a smaller community than ingress-nginx, so a Stack
Overflow answer is likelier to be about the other one. Its annotations are its own (`haproxy.org/...`),
so the Ingress objects here are not portable to another controller without edits - the routing is, the
annotations are not. And a stats page has no history: it shows *now*, so it is a debugging tool, not a
monitoring system. Seq and Jaeger remain where the history lives.

## Decision 3 - One hostname per surface, on `localtest.me`

Compose distinguishes the ten web surfaces by port: 3000, 4200, 3001, 4201, 6001, 6002, 8080, 8081,
16686. An Ingress distinguishes them by **hostname**, because that is what an HTTP request carries and
what a real deployment uses.

Local Kubernetes therefore needs local DNS, and the usual instruction is "add these lines to your hosts
file" - which needs administrator rights, differs on Windows, and is reliably the step a reader gets
wrong.

**Every subdomain of `localtest.me` resolves to 127.0.0.1 in public DNS.** It is a real domain
maintained for this purpose, and it needs no configuration on any machine:

| Surface | Hostname |
|---|---|
| React storefront | `shop.localtest.me` |
| Angular storefront | `shop-ng.localtest.me` |
| React back office | `admin.localtest.me` |
| Angular back office | `admin-ng.localtest.me` |
| Storefront BFF | `api.localtest.me` |
| Admin BFF | `admin-api.localtest.me` |
| Keycloak | `auth.localtest.me` |
| HAProxy stats | `haproxy.localtest.me` |
| Seq (logs) | `logs.localtest.me` |
| Jaeger (traces) | `traces.localtest.me` |

**What it costs:** a dependency on somebody else's DNS. If `localtest.me` stops resolving, or the reader
is on a machine with no internet, the local overlay breaks - and the failure looks like a Kubernetes
problem rather than a DNS one. The runbook says to check `nslookup shop.localtest.me` first for exactly
this reason, and a hosts file remains the documented fallback. Also: everything is **http**, not https.
Real TLS needs certificates, and a local cluster with a self-signed certificate trains people to click
through browser warnings - a worse habit than using http locally.

## Decision 4 - Databases run in-cluster locally, and are managed services in Azure

The local overlay runs eight PostgreSQL StatefulSets, RabbitMQ and Redis inside the cluster. The Azure
overlay does not: it removes them and points the services at Azure Database for PostgreSQL, a managed
broker and Azure Cache for Redis, through the same environment variables.

This is possible only because of [ADR-0017](0017-cloud-portable-architecture.md): no service names a
provider, and every connection is a configuration value. The overlay changes a ConfigMap and a Secret,
not a line of code.

**Why not run the databases in the cluster in Azure too:** you can, and for a stateless workload
Kubernetes is excellent. A database is the opposite of stateless. Running one well means owning backups,
point-in-time restore, failover, minor-version upgrades and storage growth - none of which is what this
project teaches, and all of which the managed service does. Running PostgreSQL locally is a convenience
that costs nothing when the data is disposable.

**What it costs:** the two overlays diverge in the one place where divergence hurts most - the thing
holding the data. A bug that appears only against Azure Database for PostgreSQL will not appear locally.
That is the standard trade, and it is why the local overlay pins the same major version of PostgreSQL
that the managed service runs.

## Alternatives rejected

**Compose only, and skip Kubernetes.** Defensible for a demo, and it is what Phases 1 to 10 do. It
leaves out the entire subject of orchestration - replicas, probes, rolling updates, draining, resource
limits, horizontal scaling - which is a large fraction of what an interviewer means by "have you run
this in production".

**Managed Kubernetes only (AKS), with no local option.** Simpler to write, and it puts a credit card
between the reader and their first `kubectl get pods`. A local cluster costs nothing and teaches the
same objects.

**A service mesh (Istio, Linkerd).** It would answer mTLS, retries and traffic splitting properly, and
it is what a large organisation reaches for. It also roughly doubles the concept count before the first
pod runs, and this project already demonstrates retries in application code where they can be read. A
non-goal, recorded as one.
