# Running the platform on Kubernetes, locally

Thirty workloads, ten hostnames, no cloud account and no cost. Roughly fifteen minutes the first time,
most of it spent building images.

New to all of this? Read [From Docker to Kubernetes](from-docker-to-kubernetes.md) first - this page
assumes you know what a pod and a Service are.

---

## What you need

| | |
|---|---|
| **Docker Desktop** | Already required for `docker compose`. |
| **kubectl** | Ships with Docker Desktop, at `C:\Program Files\Docker\Docker\resources\bin\kubectl.exe`. Check with `kubectl version --client`. |
| **A cluster** | Either Docker Desktop's built-in Kubernetes, or `kind`. Both below. |
| **About 8GB of free RAM** | Thirty pods plus a control plane. If Compose already runs on this machine, so will this. |

### Choosing a cluster

**Docker Desktop's Kubernetes** - Settings, Kubernetes, "Enable Kubernetes", Apply & Restart. It shares
the local image store, so images you build are immediately usable, and a Service of type LoadBalancer
is published straight onto localhost. Easiest, if you are happy to restart Docker Desktop (which stops
every container you currently have running).

**kind** - "Kubernetes IN Docker": the cluster is a Docker container. Nothing about Docker Desktop
changes, nothing else stops, and `kind delete cluster` leaves no trace. It needs one extra step
(copying images in) which the script handles. This is what the manifests were developed and verified
against.

```powershell
winget install Kubernetes.kind          # or: choco install kind
kind create cluster --config deploy/k8s/kind-cluster.yaml
```

That config publishes the cluster's ports 30080, 30443 and 31024 onto your machine's 80, 443 and 1024.
Read [`deploy/k8s/kind-cluster.yaml`](../../deploy/k8s/kind-cluster.yaml) - it explains all four hops a
request takes.

---

## The short version

```powershell
./scripts/k8s-up.ps1
```

Builds the images, copies them into the cluster, installs the ingress controller, applies the
application, waits for every pod, prints the URLs. `-SkipBuild` reuses the images you already have.

To tear it down:

```powershell
./scripts/k8s-down.ps1                  # delete everything, data included
./scripts/k8s-down.ps1 -KeepData        # keep the databases
./scripts/k8s-down.ps1 -DeleteCluster   # delete the kind cluster entirely
```

---

## The long version, and why each step exists

### 1. Build the images

```powershell
cd deploy
docker compose build
```

**Kubernetes never builds anything.** It runs images that already exist. This is the single biggest
day-one difference from Compose, where `up` quietly builds for you - and the reason a change to your
code appears to have no effect: nothing rebuilt it.

### 2. Copy them into the cluster (kind only)

```powershell
kind load docker-image --name ecommerce ecommerce-catalog-api:latest
```

A kind node is a container with its **own** image store. Your local images are invisible to it. Skip
this and every pod sits in `ErrImagePull`, trying to fetch `ecommerce-catalog-api:latest` from Docker
Hub, where it obviously does not exist:

```
Failed to pull image "ecommerce-catalog-api:latest": pull access denied,
repository does not exist or may require authorization
```

Docker Desktop's Kubernetes shares the image store, so this step does not apply there.

### 3. Install the ingress controller

```powershell
kubectl apply -k deploy/k8s/ingress-controller
kubectl rollout status -n haproxy-controller deploy/haproxy-kubernetes-ingress
```

**Do this before the application.** The controller claims Ingress objects as it observes them, and
routes that exist before it is watching can go unclaimed - which presents as HAProxy answering `404` to
every hostname while `kubectl get ingress` shows five perfectly healthy objects. If you ever hit that,
the recovery is one line:

```powershell
kubectl annotate ingress -n ecommerce --all resync=1 --overwrite
```

Any change to the object is enough; the annotation is just the cheapest change to make.

### 4. Apply the application

```powershell
kubectl kustomize --load-restrictor LoadRestrictionsNone deploy/k8s/overlays/local | kubectl apply -f -
```

Two things in that line deserve explanation.

**Why `kustomize | apply` rather than `apply -k`.** The Keycloak realm ConfigMap is generated from
`identity/keycloak/realm-export.json`, which lives outside the kustomization directory - deliberately,
because Compose reads the same file and two realms would drift. Kustomize refuses to read outside its
own directory by default (a kustomization that can read anything on disk produces different output
depending on where it ran), and `kubectl apply -k` has no flag to relax that. `kubectl kustomize` does.

**Read before you apply.** Drop the `| kubectl apply -f -` and you get the exact YAML that is about to
be submitted. There is no templating language in the way, which is the whole reason this project chose
Kustomize over Helm ([ADR-0024](../adr/0024-kubernetes-topology.md)).

### 5. Wait

```powershell
kubectl wait --for=condition=ready pod --all -n ecommerce --timeout=600s
```

Cold start order: databases (seconds), RabbitMQ (~30s), Keycloak (2-4 minutes - it runs Quarkus
augmentation and its database migrations together on first boot), then everything that needs a token.

Watch it happen:

```powershell
kubectl get pods -n ecommerce --watch
```

---

## The URLs

Every one of these resolves to 127.0.0.1 from public DNS. Nothing to add to a hosts file.

| Surface | URL |
|---|---|
| React storefront | http://shop.localtest.me |
| Angular storefront | http://shop-ng.localtest.me |
| React back office | http://admin.localtest.me |
| Angular back office | http://admin-ng.localtest.me |
| Storefront BFF | http://api.localtest.me |
| Admin BFF | http://admin-api.localtest.me |
| Keycloak | http://auth.localtest.me |
| Seq (logs) | http://logs.localtest.me |
| Jaeger (traces) | http://traces.localtest.me |
| **HAProxy stats** | **http://localhost:1024** |

Seed users are unchanged: `customer`, `support`, `catalogmgr`, `ordermgr`, `administrator`, all with
password `Passw0rd!`.

> **If a hostname does not resolve**, check DNS before Kubernetes: `nslookup shop.localtest.me` should
> answer `127.0.0.1`. `localtest.me` is a real public domain and needs internet access to resolve. On a
> machine with none, add the names to your hosts file pointing at 127.0.0.1.

---

## Deploying a code change

```powershell
docker compose build catalog-api                                  # 1. rebuild
kind load docker-image --name ecommerce ecommerce-catalog-api:latest   # 2. copy in (kind only)
kubectl rollout restart deployment/catalog-api -n ecommerce       # 3. roll
kubectl rollout status deployment/catalog-api -n ecommerce        # 4. watch
```

**Why `rollout restart` and not `apply`.** The manifest still says `ecommerce-catalog-api:latest`.
Nothing in the pod spec changed, so an apply is a no-op and Kubernetes does nothing at all - correctly,
because you told it the desired state and the desired state is unchanged. `rollout restart` stamps the
pod template with a timestamp annotation, which *is* a change, which triggers a normal rolling update.

This is also the argument against `:latest` in a real deployment. With `sha-<commit>` tags, changing
the tag *is* the change, and the deploy is an ordinary apply with a full history to roll back through.

---

## What Compose does that this does not

Honest limitations of the local Kubernetes setup:

- **No live reload.** Compose can bind-mount source. Here every change is a rebuild.
- **Slower loop.** Build, load, roll is roughly a minute against Compose's ten seconds.
- **More moving parts to go wrong**, and the failure is usually further from the cause.

For day-to-day development, `docker compose up` is still the right tool, and this project keeps it as
the primary path. Kubernetes is here to show how the same system is *operated*.

---

## Where next

- [Watching traffic](watching-traffic.md) - the stats page, and a rolling update with live connections
- [Debugging](debugging.md) - the commands, and the failures this project actually hit
- [Sessions and upgrades](sessions-and-upgrades.md) - what a logged-in user experiences during a deploy
