# From Docker to Kubernetes, one problem at a time

This page assumes you know nothing about any of it. Each section exists because the previous one has a
problem it cannot solve. That is genuinely how these tools arrived, and it is the only way the
vocabulary sticks.

If you already know Docker and Compose, skip to [Step 3](#step-3-the-problem-compose-cannot-solve).

---

## Step 0: the problem before Docker

You have an application. It runs on your machine. You put it on a server and it does not run, because
the server has a different version of .NET, a different OpenSSL, a different locale that changes how
your database sorts things.

"Works on my machine" is not a joke about carelessness. It is a real statement about how much of a
running program lives outside the program.

---

## Step 1: Docker - shipping the machine with the program

A **container image** is your application *plus everything it needs to run*: the runtime, the system
libraries, the files. Not a whole operating system - it shares the host's kernel - but everything
above that.

A **container** is one running instance of an image.

```
image     = the recipe, a file, identical everywhere        (ecommerce-catalog-api:latest)
container = a dish cooked from it, running right now        (a process, with its own filesystem view)
```

You can start ten containers from one image. They do not see each other's files or processes.

The three commands that matter:

```bash
docker build -t myapp .      # recipe -> image
docker run myapp             # image  -> running container
docker ps                    # what is running
```

**What this solved:** the same image runs identically on your laptop, on CI and on a server.

**What it did not solve:** this system has thirty containers. Starting them by hand, in the right
order, with the right network and the right thirty environment variables, is not something anybody
will do twice.

---

## Step 2: Docker Compose - one file for many containers

`docker-compose.yml` describes all of them, and one command starts them:

```bash
docker compose up -d
```

Compose adds three ideas worth naming, because Kubernetes has all three under different names:

| Compose idea | What it does |
|---|---|
| **service** | A named container with its image, environment and ports |
| **network** | Containers on a network reach each other **by service name**: `http://catalog-api:8080` |
| **volume** | Storage that outlives the container, so a database restart does not lose data |

That second one is the important one. `catalog-api` is not a hostname anybody configured - Compose runs
a DNS server that resolves service names to whichever container is currently running.

**What this solved:** thirty containers, one command, one file, in dependency order.

---

## Step 3: the problem Compose cannot solve

Compose runs containers **on one machine**, and it stops there. Ask it any of these and it has no
answer:

1. **A container dies at 3am. What restarts it?** `restart: unless-stopped` restarts the process. If
   the *machine* dies, nothing restarts anything.
2. **One machine is not enough. How do I use five?** Compose has no concept of a second machine.
3. **How do I deploy a new version without dropping requests?** `docker compose up` stops the old
   container and starts the new one. Everything in flight is lost, and the service is down until the
   new one boots.
4. **This service needs three copies under load and one at night. How?** Compose can scale a service,
   but nothing decides *when*, and nothing spreads them across machines.
5. **A container is running but wedged - accepting connections, answering nothing. Who notices?**
   Compose's healthcheck marks it unhealthy. Nothing acts on that.

Every one of those is a *cluster* problem: many machines, treated as one pool.

---

## Step 4: Kubernetes - declaring what should be true

The single idea underneath all of Kubernetes:

> **You describe the state you want. A controller continuously makes reality match it.**

You never say "start a container". You say "there should be three of these", and a control loop keeps
checking: are there three? No? Start one. Four somehow? Stop one. Forever, without you.

That is why `kubectl apply` is the normal verb rather than `kubectl create` or `kubectl start`. You
are submitting a description, not issuing a command.

### The objects, and their Compose equivalents

| Kubernetes | Compose | What it is |
|---|---|---|
| **Pod** | (a container) | The smallest unit. One or more containers that share a network address and are scheduled together. Almost always one container. |
| **Deployment** | `service:` | "Keep N pods of this image running, and replace them like *this* when the image changes." |
| **Service** | (the network) | A stable name and address in front of whichever pods currently exist. `http://catalog-api:8080`, exactly as in Compose. |
| **StatefulSet** | `service:` + `volume:` | Like a Deployment, but pods get stable names and their own storage that survives them. For databases. |
| **ConfigMap** | `environment:` | Non-secret configuration. |
| **Secret** | `environment:` (with a password in it) | Configuration that is a credential. |
| **PersistentVolumeClaim** | `volume:` | "I need 1GiB of storage that outlives this pod." |
| **Ingress** | `ports:` | HTTP routing from outside: which hostname goes to which Service. |
| **Namespace** | the project name | A name scope. Everything in this project lives in `ecommerce`. |

**A pod is not a container.** A pod is the unit Kubernetes schedules, and it contains containers. The
distinction matters exactly twice: when you want two containers glued together (a sidecar - a log
shipper, a proxy), and when you read an error message that says "pod" and means the wrapper.

### What actually happens when you apply a Deployment

```
kubectl apply -f deployment.yaml
   |
   v
API server            stores the object in etcd. Nothing is running yet.
   |
   v
Deployment controller sees a Deployment with no ReplicaSet. Creates one.
   |
   v
ReplicaSet controller sees a ReplicaSet wanting 2 pods and 0 existing. Creates 2 pod objects.
   |
   v
Scheduler             sees 2 pods with no node assigned. Picks nodes with enough free
                      cpu/memory, writes the node name onto each pod.
   |
   v
kubelet (on the node) sees a pod assigned to it. Pulls the image, starts the container,
                      then reports status back forever.
```

Five independent loops, each watching for a difference and making one small correction. Nothing
orchestrates them in sequence. **Understanding this diagram is most of understanding Kubernetes** - and
it is why the answer to "why is my pod not running?" is always "ask which loop stopped":

- No Deployment? Your apply failed.
- Deployment but no pods? The ReplicaSet controller is unhappy - check `kubectl describe deploy`.
- Pods `Pending`? The scheduler could not place them. Almost always insufficient cpu/memory.
- Pods `ContainerCreating` for a long time? The kubelet is pulling an image or mounting a volume.
- Pods `CrashLoopBackOff`? The container starts and exits. Your application, your logs.

---

## Step 5: what a Service actually is

This is the object people find most surprising, so it is worth being precise.

A Service is **not a process**. Nothing is listening on its behalf. It is a rule programmed into every
node's network stack: "traffic to this virtual IP gets rewritten to one of these pod IPs".

```
                        Service: catalog-api            (a stable virtual IP, 10.96.x.x)
                        selector: app=catalog-api
                                |
        +-----------------------+-----------------------+
        |                       |                       |
   pod 10.244.0.5          pod 10.244.0.9          pod 10.244.0.14
   app=catalog-api         app=catalog-api         app=catalog-api
```

The Service finds its pods **by label**, not by name. It is a live query: any pod that gains the label
joins, any pod that loses it or dies leaves. This is why deleting and recreating pods changes nothing
for callers.

**The most common Kubernetes bug follows directly from that.** If the Service's `selector` and the
pods' `labels` do not match, the Service matches *nothing*, and every request fails with connection
refused - while `kubectl get pods` shows everything perfectly healthy. One command diagnoses it:

```bash
kubectl get endpoints catalog-api -n ecommerce
#  ENDPOINTS  <none>      <- the Service is pointing at nothing
```

---

## Step 6: how requests get in from outside

Pods and Services are internal. Nothing outside the cluster can reach either.

An **Ingress** is a routing table: "shop.localtest.me goes to the react-store Service". It is only a
description. An **ingress controller** - a real program, here HAProxy - watches for Ingress objects and
reconfigures itself to match.

```
browser                          the ingress controller reads the
   |  Host: shop.localtest.me    Host header and picks a backend
   v
[ HAProxy pod ] ---> Service react-store ---> one of the react-store pods
```

**If no controller is installed, Ingress objects do nothing at all** - they sit in the cluster, look
correct in `kubectl get ingress`, and route nothing. That is worth remembering, because it is the most
common "my Ingress does not work".

---

## Step 7: what you gained

Back to the five questions from Step 3:

1. **A container dies.** The ReplicaSet controller notices the pod count dropped and creates another.
   If the *machine* dies, the scheduler places its pods elsewhere.
2. **Many machines.** Add a node; the scheduler starts using it. Nothing else changes.
3. **Deploy without dropping requests.** A rolling update starts a new pod, waits for it to pass its
   readiness probe, adds it to the Service, removes an old one, repeats. With `maxUnavailable: 0` the
   capacity never dips. [Watching it happen](watching-traffic.md).
4. **Scale.** `kubectl scale deployment catalog-api --replicas=5`, or a HorizontalPodAutoscaler that
   does it from CPU.
5. **A wedged container.** The liveness probe fails, the kubelet restarts it. The readiness probe takes
   it out of the Service *first*, so users stop being sent to it before anything dramatic happens.

## And what you paid

Honesty requires the other column:

- **A great deal of new vocabulary** before the first request is served.
- **YAML in quantity.** This project's thirty workloads are about 2,000 lines of manifest, against 700
  of Compose.
- **Failures move.** A Compose failure is a container that will not start. A Kubernetes failure can be
  a scheduling problem, an image problem, a probe problem, a label problem or a DNS problem, and the
  error message is often several loops away from the cause.
- **You still need Docker.** Kubernetes runs images; it does not build them.

Compose is not a worse Kubernetes. For one machine it is the better tool, and this project keeps both:
`docker compose up` remains the way to run it locally.

---

## Where next

- [Running it locally](running-locally.md) - get the thirty workloads up
- [Watching traffic](watching-traffic.md) - the HAProxy stats page, and connections during a deploy
- [Debugging](debugging.md) - what to type when something is wrong
- [Sessions and upgrades](sessions-and-upgrades.md) - what happens to a logged-in user mid-deploy
- [Interview questions](interview-questions.md) - what this demonstrates, and what gets asked
