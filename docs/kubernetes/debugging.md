# Debugging a system running on Kubernetes

The hard part of Kubernetes is not the YAML. It is that a failure surfaces several control loops away
from its cause, so the error you are shown is rarely the error that happened.

This page is in two halves: **the commands**, and **the failures this project actually hit** - every
one of the second group was found by running it, not by reading it.

---

## The first four commands

Ninety percent of debugging is these, in this order.

```bash
# 1. WHAT is wrong - which pods are not 1/1 Running
kubectl get pods -n ecommerce | grep -v "1/1"

# 2. WHY - Events at the bottom are what you came for
kubectl describe pod -n ecommerce <pod-name>

# 3. What the APPLICATION said
kubectl logs -n ecommerce <pod-name>
kubectl logs -n ecommerce <pod-name> --previous     # the run that CRASHED, not the one retrying

# 4. What is happening cluster-wide, newest last
kubectl get events -n ecommerce --sort-by=.lastTimestamp | tail -20
```

`--previous` is the one people forget. When a pod is in `CrashLoopBackOff`, `kubectl logs` shows the
*current* attempt, which has usually not failed yet. The evidence is in the run before it.

### Reading a pod status

| Status | Which loop stopped | First thing to check |
|---|---|---|
| `Pending` | The **scheduler** could not place it | `describe` -> "0/1 nodes are available: insufficient cpu". Resource requests, not a broken image. |
| `ContainerCreating` | The **kubelet** is preparing it | Pulling an image or mounting a volume. Stuck for minutes means a bad image name or an unbound PVC. |
| `ErrImagePull` / `ImagePullBackOff` | The image does not exist where it is being looked for | On kind: you forgot `kind load docker-image`. |
| `CrashLoopBackOff` | The container starts and exits | `logs --previous`. This is your application. |
| `CreateContainerConfigError` | The pod spec references something missing | A ConfigMap or Secret that does not exist, or a securityContext the image cannot satisfy. |
| `OOMKilled` (in `describe`) | The kernel killed it for exceeding its memory limit | Exit code 137. Raise the limit or find the leak. |
| `Running` but `0/1` | It is up but **not ready** | The readiness probe is failing. `describe` shows the probe result. |

### Getting inside

```bash
kubectl exec -it -n ecommerce deploy/catalog-api -- sh          # a shell in the container
kubectl port-forward -n ecommerce svc/catalog-api 8080:8080     # reach an internal Service directly
kubectl port-forward -n ecommerce svc/catalog-db 15432:5432     # then psql -h localhost -p 15432
```

`port-forward` is the Kubernetes equivalent of Compose's published ports, on demand. It is how you talk
to a service that has deliberately no route from outside - which is all nine of them here.

### Testing DNS and connectivity from inside the cluster

```bash
kubectl run tmp --rm -it --image=curlimages/curl -n ecommerce --restart=Never -- sh
# then:
curl -s http://catalog-api:8080/health/ready
nslookup catalog-db
```

A throwaway pod on the cluster network. This is how you tell "the service is broken" from "the caller
cannot reach it", and those have completely different fixes.

---

## Logs: three places, three purposes

| | Where | Use it for |
|---|---|---|
| **kubectl logs** | The container's stdout | One pod, right now. Fast, no dependencies, works when everything else is broken. |
| **Seq** | http://logs.localtest.me | Structured search across every service. Filter by correlation ID and follow one request across nine services. |
| **Jaeger** | http://traces.localtest.me | *Timing*. Which span was slow, what called what, where the 400ms went. |

`kubectl logs` has a real limitation worth knowing: **it only has the current pod's output.** Delete the
pod and the logs are gone. That is precisely why Seq exists - shipping logs off the node is not
optional once pods are disposable.

```bash
kubectl logs -n ecommerce -l app.kubernetes.io/name=catalog-api --all-containers --tail=100 -f
kubectl logs -n ecommerce -l app.kubernetes.io/part-of=ecommerce --tail=20 | grep -i error
```

The `-l` form follows *every* pod of a Deployment at once, which is what you want when there are three
replicas and you do not know which served the request.

---

## The failures this project actually hit

Each of these cost real time on a real cluster. They are in the manifests as comments too, but they are
worth reading together, because the pattern is the same every time: **the message names a symptom
several steps from the cause.**

### 1. nginx dies with a permission error, as root

```
[emerg] chown("/var/cache/nginx/client_temp", 101) failed (1: Operation not permitted)
```

The container **is** root. The pod dropped `ALL` capabilities, and root without `CAP_CHOWN` cannot
chown anything.

This is the clearest demonstration you will get that **root is a set of capabilities, not a magic
flag**. The fix is to add back the three nginx actually needs - `CHOWN`, `SETGID`, `SETUID` - and no
more. `NET_BIND_SERVICE` is deliberately not among them, because the image listens on 8080.

### 2. `runAsNonRoot` refuses to start a non-root image

```
Error: container has runAsNonRoot and image has non-numeric user (app),
       cannot verify user is non-root
```

The Dockerfile says `USER app` - a **name**. The kubelet does not resolve names inside the image, so it
cannot tell whether `app` is uid 0 or uid 1654, and it refuses rather than guessing.

Add `runAsUser: 1654`. Find the number with:

```bash
docker run --rm --entrypoint sh ecommerce-catalog-api:latest -c id
```

### 3. Keycloak's log simply stops

```
INFO [QuarkusJpaUpdaterProvider] Initializing database schema.
<nothing>
```

No error, no stack trace. `kubectl describe pod` gives it away:

```
Last State:  Terminated
Reason:      OOMKilled
Exit Code:   137
```

The kernel killed the process, so the process never got to say anything. **Exit 137 (128 + SIGKILL) in
a pod with a memory limit is an OOM kill nearly every time.**

The lesson is about *when* memory peaks: Keycloak needs the most it will ever need on its **first**
start, running Quarkus augmentation and Liquibase migrations together. A limit sized to steady-state
usage produces a service that works perfectly until the day it restarts into a new version. 1536Mi was
not enough; 2Gi is.

### 4. Every hostname returns 404, and everything looks healthy

`kubectl get ingress` shows five objects with the right hosts and the right class. HAProxy is running.
Every request gets a plain 404 - not a connection refused, which is the tell: something *is* answering.

Turn on the controller's debug logging:

```bash
kubectl logs -n haproxy-controller deploy/haproxy-kubernetes-ingress | grep ignored
# ingress 'ecommerce/storefront' ignored: no matching
```

Two separate causes hit this project:

- **`--ingress.class=haproxy` on the controller.** It looks like the obviously right flag. It is not:
  it switches the controller to matching the *legacy* `kubernetes.io/ingress.class` annotation, so
  every Ingress using the modern `ingressClassName` is ignored. Leave it at its default and let the
  IngressClass object do the work.
- **Applying the Ingresses before the controller existed.** It claims objects as it observes them.
  Install the controller first. If you are already in this state:
  `kubectl annotate ingress -n ecommerce --all resync=1 --overwrite`.

**The general lesson is the useful one:** `kubectl get ingress` proves the object exists. It says
nothing about anybody having *read* it. When a controller implements an object, the controller's log is
the source of truth, not the object.

### 5. A Service with no endpoints

Not hit here, because the labels were generated - but it is the most common Kubernetes bug in
existence, so know the shape:

```bash
kubectl get endpoints catalog-api -n ecommerce
# ENDPOINTS   <none>
```

The Service's `selector` does not match any pod's `labels`. Everything reports healthy; every request
gets connection refused. `<none>` is the whole diagnosis.

This is also why [`base/kustomization.yaml`](../../deploy/k8s/base/kustomization.yaml) sets
`includeSelectors: false` on its common labels - adding a label to selectors would change a
Deployment's `spec.selector`, which is **immutable**, and the only fix is to delete the Deployment.

---

## A checklist for "it does not work"

Work outside in. Each step tells you which half of the system to stop looking at.

```bash
# 1. Does DNS resolve? (localtest.me is real public DNS)
nslookup shop.localtest.me                     # expect 127.0.0.1

# 2. Is the ingress controller running and did it claim the route?
kubectl get pods -n haproxy-controller
curl -s 'http://localhost:1024/;csv' | cut -d, -f1,2,18 | grep ecommerce

# 3. Does the Service have endpoints?
kubectl get endpoints -n ecommerce react-store

# 4. Are the pods ready?
kubectl get pods -n ecommerce -l app.kubernetes.io/name=react-store

# 5. Does the pod answer when you bypass everything?
kubectl port-forward -n ecommerce deploy/react-store 9000:8080
curl -s localhost:9000/healthz

# 6. What did it say?
kubectl logs -n ecommerce deploy/react-store --tail=50
```

If step 5 works and step 1 does not, the application is fine and the problem is routing. That single
distinction saves more time than any other habit here.

---

## Commands worth memorising

```bash
kubectl get pods -n ecommerce -o wide --watch          # live, with node and IP
kubectl top pods -n ecommerce                          # actual cpu/memory (needs metrics-server)
kubectl rollout undo deployment/catalog-api -n ecommerce   # back to the previous ReplicaSet
kubectl rollout history deployment/catalog-api -n ecommerce
kubectl scale deployment/catalog-api --replicas=3 -n ecommerce
kubectl delete pod -n ecommerce <name>                 # safe: the ReplicaSet makes another
kubectl get all -n ecommerce                           # NOT everything - no secrets, no ingresses
```

`kubectl get all` is famously misnamed: it lists a handful of common kinds and quietly omits ConfigMaps,
Secrets, Ingresses and PVCs. Trusting it to mean "all" is how people conclude a namespace is empty when
it is not.

---

## Where next

- [Watching traffic](watching-traffic.md) - the stats page and rolling updates
- [The Compose runbook](../operations/runbook.md) - incident procedures that apply to both
- [Interview questions](interview-questions.md)
