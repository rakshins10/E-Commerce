# Sessions, connections, and what a logged-in user experiences during a deploy

"Can I see who is logged in, and what happens to them when I upgrade the containers?"

That is really three questions with three different answers, and conflating them is the usual source of
confusion. This page separates them.

---

## Three things called "a session"

| | Lives in | Survives a pod restart? | Where to look |
|---|---|---|---|
| **A TCP connection** | HAProxy and the kernel | No - but it is finished cleanly first | http://localhost:1024 |
| **A user session** | Keycloak's database | **Yes** | http://auth.localtest.me -> Sessions |
| **A basket** | Redis | **Yes** | `kubectl exec` into redis, or just look at the app |

Nothing on that list lives in an application pod. That is the entire reason a deploy is uneventful, and
it was an architecture decision long before it was an operations one.

---

## 1. Connections - the live view

The HAProxy stats page shows current sessions per pod, updated as you refresh.

```bash
curl -s 'http://localhost:1024/;csv' \
  | awk -F, '$1 ~ /^ecommerce_/ && $2 ~ /^SRV/ && $18=="UP" {print $1, $2, "cur="$5, "total="$8}'
```

This is a *transport* view. HAProxy has no idea who anybody is - it sees connections, not people. It
answers "where is traffic going right now", which is exactly the question during a deploy and exactly
the wrong question for "is Alice signed in".

Full column guide in [Watching traffic](watching-traffic.md).

---

## 2. User sessions - who is actually signed in

**http://auth.localtest.me** -> Administration Console -> realm `ecommerce` -> **Sessions**.

Every signed-in user: username, IP, start time, last access, and which client they used (React
storefront, Angular back office, and so on). You can sign an individual out, or sign out a whole realm.

From the command line:

```bash
TOKEN=$(curl -s \
  -d 'client_id=admin-cli' -d 'username=admin' -d 'password=dev_only_kc_admin_pw' \
  -d 'grant_type=password' \
  http://auth.localtest.me/realms/master/protocol/openid-connect/token | jq -r .access_token)

# Sessions per client
curl -s -H "Authorization: Bearer $TOKEN" \
  http://auth.localtest.me/admin/realms/ecommerce/client-session-stats | jq

# Everyone currently signed in to the React storefront
CID=$(curl -s -H "Authorization: Bearer $TOKEN" \
  'http://auth.localtest.me/admin/realms/ecommerce/clients?clientId=storefront-react' | jq -r '.[0].id')
curl -s -H "Authorization: Bearer $TOKEN" \
  "http://auth.localtest.me/admin/realms/ecommerce/clients/$CID/user-sessions" | jq
```

**This list is completely independent of your pods.** Restart every application pod in the cluster and
it does not change by one row, because no application pod ever knew about it.

---

## 3. What actually happens to a user during a rolling update

Take a user browsing the storefront, signed in, with three things in their basket. You run:

```bash
kubectl rollout restart deployment/react-store deployment/storefront-bff -n ecommerce
```

Second by second:

**t+0** - Kubernetes creates one new `react-store` pod. `maxSurge: 1` permits the extra; nothing has
been taken away yet. The user is unaffected because nothing has changed for them.

**t+2s** - The new pod passes its readiness probe and is added to the Service. HAProxy sees a new
endpoint and starts sending it traffic. Its `total` in the stats page begins climbing from zero.

**t+2s** - Simultaneously, one old pod is marked for deletion. Two things now race, and this is the
interesting moment:

- the endpoints controller removes it from the Service, which must propagate to HAProxy
- the kubelet is about to send it `SIGTERM`

`preStop: sleep 5` holds the `SIGTERM` back for five seconds. In that window the endpoint removal
propagates, so by the time the process is asked to stop, **nothing is being sent to it any more**. This
is the setting that turns "a handful of 502s on every deploy" into zero.

**t+7s** - `SIGTERM` arrives. nginx (or ASP.NET Core) finishes in-flight requests and exits. The user's
current page load, if it happened to be on this pod, completed normally.

**t+8s** - Repeat for the second old pod.

**Throughout** - the user's browser holds a JWT. Every request carries it. Whichever pod receives it
validates the signature against Keycloak's public key, which it fetched at startup and caches. **No pod
needs to have seen this user before.** Their basket is in Redis, keyed by their user id, so the new pod
reads exactly what the old one wrote.

**Net effect: nothing visible.** No re-login, no lost basket, no failed request.

### Try it

1. Sign in at http://shop.localtest.me as `customer` / `Passw0rd!`
2. Add items to the basket
3. `kubectl rollout restart deployment/react-store deployment/storefront-bff -n ecommerce`
4. Keep clicking

You stay signed in, the basket is intact, and the stats page shows the traffic moving underneath you.

---

## What would break this, and what it would cost

The behaviour above is not free. Three architecture decisions pay for it, and it is worth knowing what
happens if any of them is reversed - this is exactly what an interviewer is probing.

### If the BFF kept sessions in memory

Every restart signs everybody out. The usual remedy is **session affinity** ("sticky sessions"): the
load balancer pins each user to one pod.

That fixes the symptom and makes deploys *worse*. Draining a pod now means deliberately breaking every
user pinned to it, so you either accept that or wait for their sessions to expire before finishing the
rollout - which can take an hour. It also makes load uneven, because users are not distributed evenly.

This project has **no session affinity anywhere**, and that is a positive statement about the
architecture, not an omission.

### If the basket lived in the pod

The user's cart would vanish on every deploy and every crash. Redis is a separate deployment precisely
so a pod is disposable. Note the corollary that was accepted at the time: Redis here runs with
persistence **off**, so a *Redis* restart does empty every basket. That was a deliberate trade
([ADR-0003](../adr/0003-postgresql-and-polyglot-persistence.md)) - an abandoned cart should expire - and it is worth
saying out loud rather than discovering.

### If the saga were stateful in memory

`ordering-saga` runs at **replicas: 1** for an honest reason, spelled out in
[`services.yaml`](../../deploy/k8s/base/services.yaml): it runs a background sweeper for stuck orders,
and two replicas would sweep the same rows at once and could compensate an order twice. Its *state* is
in PostgreSQL, so a restart is safe - it is the *sweeping* that is not yet safe to run twice.

Making it safe to scale means leader election, or claiming rows with `SELECT ... FOR UPDATE SKIP
LOCKED`. Both are well understood and neither is done here. A single replica with the reason written
down beats two replicas that silently double-refund.

---

## Draining a node, not just a pod

Same mechanism, one level up. Before rebooting a machine:

```bash
kubectl drain <node> --ignore-daemonsets --delete-emptydir-data
```

That marks the node unschedulable and evicts its pods politely - each one going through the same
readiness removal and `preStop` pause. The pods are recreated on other nodes.

The object that makes this safe is a **PodDisruptionBudget**: "never voluntarily take me below 1
replica". Without one, `drain` can evict both replicas of a two-replica Deployment simultaneously and
you get a real outage during what you thought was routine maintenance.

> **Not in this project, deliberately.** The local cluster has one node, so there is nothing to drain
> *to* - a PDB would block the drain entirely rather than protect anything. It is listed as a non-goal
> here so it is a known gap rather than an oversight; a multi-node cluster wants one per
> user-facing Deployment.

---

## Where next

- [Watching traffic](watching-traffic.md) - the stats page in detail
- [Debugging](debugging.md) - when the deploy is not uneventful
- [Interview questions](interview-questions.md) - these three answers come up constantly
