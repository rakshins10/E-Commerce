# Kubernetes

The same thirty containers `docker compose up` runs, running instead on a Kubernetes cluster - with
replicas, health probes, rolling updates that drop no requests, and a live view of every connection.

Compose stays the primary way to run this project locally. Kubernetes is here to show how the same
system is **operated**: what happens when a container dies, how a new version reaches users, and who is
connected while it does.

---

## Start here

| | |
|---|---|
| **[From Docker to Kubernetes](from-docker-to-kubernetes.md)** | Assumes you know nothing. Containers, Compose, then each Kubernetes object as the answer to a problem Compose cannot solve. |
| **[Running it locally](running-locally.md)** | Thirty workloads on your machine in about fifteen minutes, no cloud account. |
| **[Watching traffic](watching-traffic.md)** | The HAProxy stats page. Live connections per pod, and a rolling update with a request counter proving nothing failed. |
| **[Sessions and upgrades](sessions-and-upgrades.md)** | Three different things called "a session", who is signed in, and what a user experiences mid-deploy. |
| **[Debugging](debugging.md)** | The commands, and the five failures this project actually hit on a real cluster. |
| **[Interview questions](interview-questions.md)** | What this demonstrates, with the traps marked. |

---

## The shortest possible version

```powershell
kind create cluster --config deploy/k8s/kind-cluster.yaml
./scripts/k8s-up.ps1
```

Then http://shop.localtest.me, and http://localhost:1024 for the traffic view. Every hostname resolves
to 127.0.0.1 from public DNS - nothing to configure.

```powershell
./scripts/k8s-down.ps1 -DeleteCluster
```

---

## How the manifests are laid out

```
deploy/k8s/
├── base/                     the application: 12 .NET workloads, 4 web apps, config, routes
├── infrastructure/
│   ├── data/                 9 PostgreSQL, RabbitMQ, Redis        <- the Azure overlay omits this
│   └── platform/             Keycloak, Seq, Jaeger
├── ingress-controller/       HAProxy. Cluster infrastructure, installed once, separately
├── overlays/
│   ├── local/                local images, fewer replicas, laptop-sized requests
│   └── azure/                managed databases, real hostnames, TLS
└── kind-cluster.yaml         a local cluster made of Docker containers
```

The split exists so the cloud overlay can **delete a directory**: `infrastructure/data` is left out
entirely, and managed services take its place through the same environment variables. Being able to
remove a layer and have the system still make sense is a decent test of whether a boundary is real.

Every file is heavily commented. The manifests are meant to be read.

---

## The decisions, and what they cost

Two ADRs were written before any of this code:

- **[ADR-0023](../adr/0023-runtime-configuration-for-single-page-apps.md)** - a single-page app reads
  its configuration at runtime, not at build time. This was a prerequisite: without it, deploying the
  storefront to a cluster means rebuilding it, and the artifact you tested is not the artifact you
  ship.
- **[ADR-0024](../adr/0024-kubernetes-topology.md)** - Kustomize over Helm, HAProxy over ingress-nginx,
  one hostname per surface on `localtest.me`, and in-cluster databases locally against managed ones in
  the cloud. Each with what it costs.

---

## What is deliberately not here

Recorded as known gaps rather than discovered as surprises:

- **The Azure overlay has never been applied to a real AKS cluster.** It renders correctly and is a
  worked example of the shape; expect to fix real details on first contact.
- **No PodDisruptionBudgets.** The local cluster has one node, so there is nothing to drain to - a PDB
  would block a drain rather than protect anything. A multi-node cluster wants one per user-facing
  Deployment. [Why](sessions-and-upgrades.md#draining-a-node-not-just-a-pod).
- **No HorizontalPodAutoscaler.** It needs metrics-server and a load profile worth scaling against;
  neither exists on a laptop.
- **No NetworkPolicy.** Compose's two-network split has no direct equivalent here yet; what replaces it
  is that no Ingress routes to any of the nine services, so nothing outside can reach them.
- **No service mesh.** A deliberate non-goal - it roughly doubles the concept count before the first
  pod runs ([ADR-0024](../adr/0024-kubernetes-topology.md)).
- **No TLS locally.** A self-signed certificate trains people to click through browser warnings, which
  is a worse habit than plain http on a laptop. The Azure overlay uses cert-manager.
- **`ordering-saga` runs a single replica.** Its stuck-order sweeper is not yet safe to run
  concurrently. [The honest reason](sessions-and-upgrades.md#if-the-saga-were-stateful-in-memory).
