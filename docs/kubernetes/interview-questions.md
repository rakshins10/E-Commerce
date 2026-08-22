# Kubernetes and Docker: what this demonstrates, and what gets asked

Every answer here points at code in this repository. The value of a reference project at interview is
being able to say "here is the file, and here is what it cost" rather than reciting a definition.

Where an answer has a trap in it, the trap is marked. Those are usually the real question.

---

## Docker

### "What is the difference between an image and a container?"

An image is a read-only filesystem plus metadata - a recipe. A container is one running instance with a
writable layer on top. Ten containers from one image share the image's layers on disk.

**The follow-up that separates people:** *why is an image made of layers?* Because each Dockerfile
instruction produces one, they are content-addressed, and they are shared. Rebuilding after a code
change re-uses every layer up to the change. That is why [`src/Dockerfile`](../../src/Dockerfile)
restores NuGet packages in a separate, earlier stage from the one that copies source: source changes
constantly, dependencies rarely, and putting them in that order means a code edit does not re-download
the internet.

### "How do you make a container image small and safe?"

Four things this project does, all visible in `src/Dockerfile` and `web/Dockerfile`:

- **Multi-stage build.** The SDK image compiles; the runtime image gets only the output. The final .NET
  image has no compiler, no source and no NuGet cache.
- **A non-root user.** `USER app`. Note the Kubernetes consequence: a *name* is not enough, the pod
  needs `runAsUser: 1654`, because the kubelet cannot resolve names inside an image.
- **A specific base tag**, not `latest`, so a rebuild is reproducible.
- **The web images contain no Node.** The bundle is built in one stage; nginx serves the result.

### "Why can a container not read environment variables in a browser app?"

Because the code runs in the *user's browser*, not in the container. This is
[ADR-0023](../adr/0023-runtime-configuration-for-single-page-apps.md), and it is a good question to be
asked because the naive answer ("use a build argument") has a real cost: one image per environment,
which means the artifact you tested is not the artifact you ship.

The answer here: the container's entrypoint renders `config.js` from environment variables **before
nginx serves its first byte**, and the app reads `window.__ECOMMERCE_CONFIG__`. One image, configured
at start.

**The detail that shows you have actually done it:** the Content-Security-Policy has to be substituted
from the same two variables. Configure only the app and you get a correctly configured application
whose every request dies with "Refused to connect", because the policy still names `localhost`.

---

## Compose and Kubernetes

### "Why not just use Docker Compose?"

Compose is a single-machine tool. It cannot answer: what restarts this if the machine dies, how do I use
five machines, how do I deploy without dropping requests, how do I scale on load, who acts when a
health check fails.

**The good answer includes the other direction:** for local development Compose is *better* here -
faster loop, bind-mountable source, one file. This project keeps both, and `docker compose up` is still
the primary path. "We replaced Compose with Kubernetes" is usually a worse answer than "we use each
where it wins".

### "Walk me through what happens when you run `kubectl apply` on a Deployment."

Five independent control loops, each watching for a difference:

1. The **API server** stores the object. Nothing is running yet.
2. The **Deployment controller** creates a ReplicaSet.
3. The **ReplicaSet controller** creates pod objects to reach the desired count.
4. The **scheduler** assigns each pod to a node with room.
5. The **kubelet** on that node pulls the image and starts the container.

**Why it matters:** it tells you where to look when something is wrong. `Pending` means the scheduler
could not place it. `ContainerCreating` means the kubelet is working. `CrashLoopBackOff` means your
application. Diagram and diagnosis table in
[From Docker to Kubernetes](from-docker-to-kubernetes.md#what-actually-happens-when-you-apply-a-deployment).

### "Deployment or StatefulSet?"

Deployment: pods are interchangeable, any one can be replaced by a fresh one. StatefulSet: pods get
stable names (`catalog-db-0`), their own PersistentVolumeClaim that survives them, and ordered
start-up and shutdown.

This project: Deployments for all twelve .NET workloads and the four web apps; StatefulSets for the
nine PostgreSQL instances and RabbitMQ. Redis is a **Deployment with no volume**, deliberately - basket
data is disposable, and saying so is more honest than a StatefulSet that implies durability nobody
wants.

**The trap:** "a StatefulSet is for anything with a volume". No. The question is whether the pods are
*interchangeable*. A Deployment with a shared volume is fine; a Deployment where each pod needs *its
own* volume is not.

### "Explain liveness, readiness and startup probes."

- **Startup**: "has it finished booting?" Suppresses the other two until it passes.
- **Readiness**: "should it get traffic?" Failing **removes the pod from the Service**.
- **Liveness**: "is it wedged?" Failing **restarts the container**.

**The trap, and it is the whole question:** never use a deep dependency check as the liveness probe. If
`/health/live` checked the database, then when the database blips, every pod fails liveness, every pod
is killed, and a recoverable outage becomes a cluster-wide restart storm at the exact moment the
database is least able to cope with a reconnection stampede.

Here `/health/live` asks only "is this process answering" and `/health/ready` checks dependencies -
[`HealthCheckExtensions.cs`](../../src/building-blocks/Observability/HealthCheckExtensions.cs).

### "How do you deploy without dropping requests?"

Three settings, and all three are needed:

1. `maxUnavailable: 0` with `maxSurge: 1` - a new pod must be ready before an old one goes.
2. A readiness probe that actually means ready.
3. `preStop: sleep 5`.

**The third is the one that gets asked about.** When a pod is deleted, `SIGTERM` and the endpoint
removal happen simultaneously and in no guaranteed order. The removal takes time to propagate to every
kube-proxy and the ingress controller, so for about a second, traffic is still arriving at a pod that
has begun shutting down. Those requests fail. This is the real cause of "we get a few 502s on every
deploy". The `preStop` pause delays `SIGTERM` without delaying the removal.

Verified here: 207 requests across a rolling restart of `react-store`, zero failures.
[Watching traffic](watching-traffic.md).

### "What is a Service, really?"

Not a process. A rule programmed into each node's network stack that rewrites traffic for a virtual IP
to one of the pod IPs matching its **label selector**. It is a live query, not a list.

**The bug that follows:** if the selector and the pod labels drift apart, the Service matches nothing.
Everything reports healthy and every request gets connection refused. `kubectl get endpoints <svc>`
showing `<none>` is the entire diagnosis.

### "Ingress versus Service versus LoadBalancer?"

- `ClusterIP` (the default): reachable only inside the cluster. All sixteen application Services here.
- `NodePort`: also on a port on every node. How the HAProxy controller is reached on a local cluster.
- `LoadBalancer`: asks the cloud provider for a real load balancer.
- **Ingress**: not a Service at all - an HTTP *routing table* that an ingress controller implements.

**The trap:** an Ingress does nothing without a controller. The objects sit there looking correct and
route nothing. This project hit it twice; both causes are in
[Debugging](debugging.md#4-every-hostname-returns-404-and-everything-looks-healthy).

### "ConfigMap or Secret?"

If it is a credential, it is a Secret. But be ready for the follow-up: **a Secret is not encrypted.**
It is base64, and base64 is an encoding. By default Secrets are stored unencrypted in etcd.

What a Secret actually buys: a separate object with its own RBAC, kept out of casual output, mountable
rather than printable, and replaceable by an external store (Key Vault via the CSI driver) without
touching a single Deployment.

This project also uses **one Secret per service** rather than one shared one - `catalog-api` gets the
catalog connection string and nothing else. Database-per-service means nothing if every pod holds every
password.

### "Requests versus limits?"

`requests` is what the scheduler reserves and uses to choose a node. `limits` is the ceiling.

**The asymmetry is the question:** exceed the *memory* limit and the kernel OOM-kills you - exit 137,
and a log that just stops. Exceed the *CPU* limit and you are merely throttled: slow, not dead.

Which is why these manifests set memory limits and deliberately set **no CPU limit**. A CPU limit
throttles a process that has spare capacity available to it, producing latency spikes that are
miserable to diagnose; the request alone protects neighbours, because the request is what the scheduler
honours.

Real example from this project: Keycloak OOM-killed at 1536Mi, every time, mid-migration. The lesson is
about *when* memory peaks - the maximum is on **first start**, running augmentation and migrations
together, not in steady state.

### "How would you move this to the cloud?"

The overlay [`deploy/k8s/overlays/azure`](../../deploy/k8s/overlays/azure/kustomization.yaml) is the
answer, and the shape of it is the point:

- It **omits** `infrastructure/data` entirely - nine PostgreSQL StatefulSets, RabbitMQ and Redis are
  replaced by managed services.
- The Services keep their names via `ExternalName`, so `catalog-db` still means the catalog database
  and the connection string is the only thing that changed.
- Not one line of application code differs, because no service references a cloud SDK
  ([ADR-0017](../adr/0017-cloud-portable-architecture.md)).

**Say the honest part:** the Azure overlay has never been applied to a real AKS cluster. It is a worked
example of the shape, which is the part that proves the architecture was portable; expect to fix real
details on first contact. Claiming otherwise is a bad trade at interview.

---

## Operations

### "How do you know who is connected, and what happens to them when you deploy?"

Three different things called "a session", and separating them is the answer:

| | Lives in | Survives a deploy? |
|---|---|---|
| TCP connection | HAProxy | No, but finished cleanly first |
| User session | Keycloak | Yes |
| Basket | Redis | Yes |

Nothing lives in an application pod, so a rolling update logs nobody out. Detail, and what would break
it, in [Sessions and upgrades](sessions-and-upgrades.md).

**The strong follow-up to volunteer:** *why is there no session affinity?* Because nothing needs it -
and sticky sessions would make deploys worse, since draining a pod then means deliberately breaking the
users pinned to it.

### "How do you debug a pod that will not start?"

`kubectl get pods` for the status, `kubectl describe pod` for the Events at the bottom, `kubectl logs
--previous` for the run that actually crashed. The status names the loop that stopped.

**The detail that shows experience:** `--previous`. On a `CrashLoopBackOff`, plain `kubectl logs` shows
the current attempt, which usually has not failed yet.

### "Where do logs go?"

Three places with three purposes: `kubectl logs` (one pod, now, works when everything else is broken),
Seq (structured search across every service, follow a correlation ID through nine of them), Jaeger
(timing - which span was slow).

**The point to make:** `kubectl logs` only holds the current pod's output. Delete the pod and it is
gone. Shipping logs off the node stops being optional the moment pods are disposable.

### "Kustomize or Helm?"

Helm is better at distributing software to other people who need values they can set without reading
your templates. Kustomize is better when the manifests are meant to be *read*, because a base is real
YAML rather than a program that emits YAML.

**Name the cost, always:** Kustomize has no loops, so the nine PostgreSQL definitions here are nine
near-identical blocks that Helm would have written from a template. That was accepted deliberately
([ADR-0024](../adr/0024-kubernetes-topology.md)) - visible duplication beats a template you have to
execute in your head.

---

## Where next

- [From Docker to Kubernetes](from-docker-to-kubernetes.md) - the concepts, built up one problem at a time
- [Concepts explained](../concepts-explained.md) - the plain-English guide to every pattern here
- [Concept map](../concept-map.md) - every pattern, where it lives, what it answers
