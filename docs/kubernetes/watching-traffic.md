# Watching traffic: who is connected, and what happens during a deploy

**http://localhost:1024**

That is HAProxy's stats page, and it is the reason this project uses HAProxy as its ingress controller
rather than the more common ingress-nginx ([ADR-0024](../adr/0024-kubernetes-topology.md)). It is a
live HTML dashboard of every backend, every pod behind it, and every connection - with no Prometheus,
no Grafana and no configuration.

---

## Reading the page

Each **backend** is one Service. Each row inside it is one **pod**.

```
ecommerce_react-store_http
  Server   Status   LastChk       Sessions(cur)  Sessions(total)  Bytes Out
  SRV_1    UP       L7OK/200      3              1,204            18.2 MB
  SRV_2    UP       L7OK/200      2              1,198            17.9 MB
  SRV_3    MAINT    -             0              0                0
```

The columns worth knowing:

| Column | What it means |
|---|---|
| **Status** | `UP` = healthy and taking traffic. `DOWN` = failing health checks. `MAINT` = an empty slot (see below). |
| **LastChk** | The result of the last health check. `L7OK/200` means an HTTP check returned 200. |
| **Sessions cur** | Connections **right now**. This is the number that moves during a deploy. |
| **Sessions total** | Cumulative since the pod joined. A new pod starts at 0 - which is how you spot it. |
| **Bytes Out** | Traffic served. Two pods with wildly different numbers means uneven balancing. |

**Why the `MAINT` rows.** HAProxy pre-allocates a fixed number of server slots per backend so it can
add pods without a reload. Empty slots sit in `MAINT`. They are not broken pods; they are room to grow.
Scale a Deployment up and watch one turn `UP`.

### Prefer it as data

```bash
# Every server, one row each
curl -s 'http://localhost:1024/;csv' | column -s, -t | less -S

# Just current sessions per pod for the storefront
curl -s 'http://localhost:1024/;csv' \
  | awk -F, '$1=="ecommerce_react-store_http" {print $2, $18, "cur="$5, "total="$8}'
```

`$18` is status, `$5` is current sessions, `$8` is total. The CSV is the same data the page renders,
and it is far easier to watch in a loop.

---

## Watching a rolling update

This is the demonstration worth doing once, deliberately.

**Terminal 1 - generate traffic and count failures.** The point is not load; it is proving that *no
request fails*.

```bash
ok=0; fail=0
while true; do
  code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 http://shop.localtest.me/)
  if [ "$code" = "200" ]; then ok=$((ok+1)); else fail=$((fail+1)); echo "FAILED: $code"; fi
done
```

**Terminal 2 - watch the pods per second.**

```bash
watch -n1 "curl -s 'http://localhost:1024/;csv' \
  | awk -F, '\$1==\"ecommerce_react-store_http\" {print \$2, \$18, \$5, \$8}'"
```

**Terminal 3 - deploy.**

```bash
kubectl rollout restart deployment/react-store -n ecommerce
kubectl rollout status  deployment/react-store -n ecommerce
```

### What you will see, and why

```
SRV_1  UP    cur=2  total=812     <- old pod, serving
SRV_2  UP    cur=3  total=798     <- old pod, serving

SRV_3  UP    cur=0  total=0       <- NEW pod appeared, no traffic yet.
                                     It is UP but has just passed readiness. maxSurge: 1 allowed it.

SRV_3  UP    cur=2  total=14      <- traffic starts arriving. Total climbing from zero
                                     is how you identify a new pod.
SRV_1  UP    cur=0  total=812     <- OLD pod: current dropped to zero, total FROZEN.
                                     It has been removed from the Service. Existing
                                     requests finished; no new ones arrived.

SRV_1  MAINT cur=0  total=0       <- the old pod is gone, its slot is free again.
```

And in terminal 1: **`fail=0`.** Verified on this project - 207 requests across a full rolling restart
of `react-store`, zero failures.

### Why nothing failed - the three settings that pay for it

None of this is automatic. Remove any one and users see errors during every deploy.

**1. `maxUnavailable: 0` with `maxSurge: 1`** ([`web.yaml`](../../deploy/k8s/base/web.yaml)) means
"never go below the desired replica count" - a new pod must be **ready** before an old one is removed.
The default is 25%/25%, which briefly runs at reduced capacity; on a two-replica service, 25% rounds to
"one of your two is gone".

**2. A readiness probe that means something.** The new pod joins the Service only when
`/healthz` (web) or `/health/ready` (services) answers. A pod that is running but not yet ready gets no
traffic at all. Delete the readiness probe and Kubernetes assumes ready-on-start, so requests arrive at
a process that has not finished booting.

**3. `preStop: sleep 5`, the least obvious one.** When a pod is deleted, two things happen **at the same
time and in no guaranteed order**: the kubelet sends `SIGTERM`, and the endpoints controller removes
the pod from its Service. That removal has to propagate to every node's kube-proxy and to HAProxy,
which takes a moment - so for a second or so after `SIGTERM`, requests are *still being routed to a pod
that has begun shutting down*.

Those requests fail. This is the actual cause of the "we get a handful of 502s on every deploy" that
teams tend to blame on their application. Sleeping in `preStop` delays the `SIGTERM` without delaying
the endpoint removal, so by the time the process is told to stop, nothing is being sent to it.

It is also why `terminationGracePeriodSeconds: 40` is not decoration: the 5s pause plus the longest
in-flight request must fit inside it, or the container is `SIGKILL`ed and you lose exactly the requests
the drain was protecting.

---

## Who is logged in? (Two different questions)

This is the question that most often gets muddled, because "sessions" means two unrelated things.

### Connections - HAProxy, per pod, right now

The stats page. TCP connections currently open, per pod. It answers "where is traffic going" and
"is one pod taking everything". It is a **live** number with no history and no identity - HAProxy does
not know who anybody is.

### User sessions - Keycloak, per person

**http://auth.localtest.me** -> Administration Console (`admin` / `dev_only_kc_admin_pw`) -> your realm
-> **Sessions**.

That lists every signed-in **user**: who, from which IP, when they started, which client (React
storefront, Angular admin), and when they last did anything. You can sign one out from there.

```bash
# The same thing from the command line
TOKEN=$(curl -s -d 'client_id=admin-cli' -d 'username=admin' -d 'password=dev_only_kc_admin_pw' \
  -d 'grant_type=password' http://auth.localtest.me/realms/master/protocol/openid-connect/token \
  | jq -r .access_token)

curl -s -H "Authorization: Bearer $TOKEN" \
  http://auth.localtest.me/admin/realms/ecommerce/client-session-stats | jq
```

### The important part: these are independent

**A rolling update does not log anybody out.**

The application holds no session state. A signed-in user's identity lives in two places, neither of
which is a pod:

- a **JWT in their browser**, which any pod can validate on its own using Keycloak's public signing key
- a **session in Keycloak**, which is what issues new tokens when the old one expires

So a pod can disappear mid-request and the user notices nothing - they land on another pod, which
validates the same token and serves the same answer. That is what "stateless" buys, and it is why
there is deliberately **no session affinity** ("sticky sessions") configured anywhere in
[`ingress.yaml`](../../deploy/k8s/base/ingress.yaml).

Prove it: sign in at http://shop.localtest.me, put things in the basket, then

```bash
kubectl rollout restart deployment/react-store deployment/storefront-bff -n ecommerce
```

Keep clicking. You stay signed in; the basket is intact (it is in Redis, not in a pod).

> **What would break this.** If the BFF kept sessions in memory, every restart would sign everybody
> out, and you would be forced into sticky sessions - which then make rolling updates *worse*, because
> draining a pod means deliberately breaking the users pinned to it. The architecture decision (tokens
> and Redis, not in-process state) is what makes the operational behaviour easy. That order of cause
> and effect is worth remembering.

---

## Everyday questions the stats page answers

| Question | Where to look |
|---|---|
| Is traffic evenly spread? | `Sessions total` across the pods of one backend. Wildly uneven means a long-lived connection is pinning a client. |
| Did my new pod actually take traffic? | Its `total` climbing from 0. Still 0 minutes later means it is `UP` but nothing is being sent - check the Service's endpoints. |
| Is a pod failing health checks? | `Status` = `DOWN`, and `LastChk` says why (`L7STS/503`, `L4CON` = cannot connect at all). |
| Is the backend down entirely? | The `BACKEND` summary row is `DOWN`, meaning no server is `UP`. Every request gets 503. |
| Am I being rate limited or is it slow? | The BFF's own rate limiter returns 429 with `Retry-After` ([ADR-0022](../adr/0022-edge-hardening-defaults.md)). HAProxy shows the request arriving; Seq shows the 429. |

**What the stats page cannot do:** it has no history. It shows *now*. For "what happened at 14:05
yesterday" you need Seq (logs) and Jaeger (traces) - see [Debugging](debugging.md). Reaching for a
dashboard when you need a timeline is a common way to waste twenty minutes.

---

## Where next

- [Debugging](debugging.md) - when the traffic is not flowing
- [Sessions and upgrades](sessions-and-upgrades.md) - the full picture of a user's experience mid-deploy
- [Interview questions](interview-questions.md) - the questions this page answers
