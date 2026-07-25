# Deployment

> **New to CI/CD?** [`concepts-explained.md`](../concepts-explained.md#22-docker-and-docker-compose) covers
> containers first. This page assumes you have read that.

Three GitHub Actions workflows, each with a distinct job:

| Workflow | Runs on | What it does |
|----------|---------|--------------|
| [`ci.yml`](../../.github/workflows/ci.yml) | Every push and PR | Builds, tests, and proves the stack boots. **Gate — nothing merges without it.** |
| [`release.yml`](../../.github/workflows/release.yml) | Push to `main`, and `v*` tags | Builds and publishes 12 container images to GitHub's registry |
| [`docs.yml`](../../.github/workflows/docs.yml) | Docs changes on `main` | Publishes this documentation as a searchable website |

---

## 1. What "deployment" means here — and what it does not

**This repository builds and publishes deployable artefacts. It does not deploy to a live server**, because
there is no server to deploy to.

That is a deliberate stopping point, not an omission. Everything up to the artefact is real: images are
built, tagged immutably, signed with provenance, scanned for vulnerabilities, and published. The final step
— `kubectl apply` or an Azure Container Apps update — is a five-line addition once a target exists, and it is
the only part that would need credentials.

**Why stop there?** Because a deploy step pointing at nothing is theatre, and it needs real cloud secrets in
a public repository. The valuable, transferable work is everything before it.

---

## 2. Why publish images at all?

`docker compose up` already builds everything locally. So why build them again in CI?

**Because building on the machine that runs it is a development convenience, not a deployment strategy.**

When you build locally, the thing you tested and the thing you shipped are *different builds*. A dependency
might resolve to a newer patch version; a base image might have been updated. Usually harmless. Occasionally
the cause of a bug that reproduces nowhere.

A published image is **immutable**. You test image `sha-abc123` and you deploy image `sha-abc123` — byte for
byte the same thing. If it breaks, you roll back to `sha-def456`, which still exists.

---

## 3. Where the images go

**GHCR** — GitHub Container Registry, at `ghcr.io`. It is GitHub's built-in registry, so there is no
account to create, no secret to manage, and permissions follow the repository's.

One image per service:

```
ghcr.io/rakshins10/e-commerce-catalog-api
ghcr.io/rakshins10/e-commerce-ordering-api
ghcr.io/rakshins10/e-commerce-storefront-bff
… twelve in total
```

Pull one:

```bash
docker pull ghcr.io/rakshins10/e-commerce-catalog-api:latest
```

> Images are private until you make them public. Repository → **Packages** → select one → **Package
> settings** → **Change visibility**.

---

## 4. Tags, and the one rule that matters

Each build produces several tags for different audiences:

| Tag | Example | Use for |
|-----|---------|---------|
| `sha-<commit>` | `sha-ba77141…` | **Deploying.** Immutable — always this exact build |
| `1.4.2` | from a `v1.4.2` git tag | Releases |
| `1.4` | from the same tag | "Latest patch of 1.4" |
| `latest` | | Local experimentation **only** |

### Never deploy `latest`

`latest` is a *moving pointer*, not a version. Deploy it and:

- **"What is running in production?"** becomes unanswerable — it depends when each instance last pulled.
- **Rollback has no target.** There is no "previous latest".
- **Two servers can run different code** while both claiming to run `latest`.

Deploy `sha-…` or the digest. This is one of the most common real-world container mistakes.

---

## 5. What else the release workflow does

Beyond building, three things worth knowing about:

**Multi-architecture builds** — every image is built for `linux/amd64` *and* `linux/arm64`, so it runs on an
Apple Silicon Mac, on AWS Graviton, and on ordinary x86 servers. Docker picks the right one automatically.

**Provenance attestation** — a cryptographically signed statement of *which commit and which workflow*
produced the image. It answers "can I prove this image came from that source code?" — the supply-chain
question that matters after incidents like SolarWinds. Anyone can verify it:

```bash
gh attestation verify oci://ghcr.io/rakshins10/e-commerce-catalog-api:latest --owner rakshins10
```

**Vulnerability scanning** — [Trivy](https://trivy.dev) scans the published images and reports findings to
the repository's **Security** tab.

> This is a *different* check from the one in `ci.yml`. NuGet audit catches vulnerable **NuGet packages** —
> our own dependencies. Trivy catches vulnerabilities in the **operating system inside the image** — the
> Debian packages in the .NET base image. Both matter, and neither finds the other's problems.

Scanning reports rather than fails, deliberately: a base-image CVE with no patch yet available should be
*visible* without blocking every merge until Microsoft ships a fix.

---

## 6. The documentation site

`docs.yml` builds `docs/` into a searchable website with [MkDocs Material](https://squidfunk.github.io/mkdocs-material/)
and publishes it to GitHub Pages:

**https://rakshins10.github.io/E-Commerce/**

Why, when Markdown already renders on GitHub? Because the stated goal is that a reader can **learn the system
from `docs/` alone**, and that needs full-text search across pages, a navigation sidebar, and reliably
rendered Mermaid diagrams. GitHub gives you none of the first two.

The reading order in the sidebar is deliberate — concepts, then architecture, then code.

### ⚠️ One-time setup you must do by hand

The Pages deployment will fail with a 404 until this is enabled:

> **Repository → Settings → Pages → Build and deployment → Source → “GitHub Actions”**

This cannot be automated: enabling Pages is an account-level action requiring permissions a workflow token
deliberately does not have.

---

## 7. What a real deployment would add

Written out because "how would you actually deploy this?" is the obvious follow-up question.

### The missing step

```yaml
# Appended to release.yml once a target exists
deploy-staging:
  needs: publish
  environment: staging        # GitHub environments give you approval gates
  steps:
    - run: |
        kubectl set image deployment/catalog-api \
          catalog-api=ghcr.io/${{ github.repository }}-catalog-api:sha-${{ github.sha }}
```

Note it deploys the **`sha-` tag**, per §4.

### Kubernetes

Compose is deliberately shaped so this is configuration, not a redesign — nothing hardcodes a hostname, all
settings arrive as environment variables, and liveness and readiness are already separate endpoints
([health checks](health-checks.md)):

| Compose | Kubernetes |
|---------|-----------|
| `healthcheck` on `/health/live` | `livenessProbe` |
| `/health/ready` | `readinessProbe` |
| `depends_on: service_healthy` | readiness gating + retries |
| `environment:` | ConfigMap |
| Secrets in `.env` | Secret, ideally via External Secrets |
| `deploy: replicas` | `replicas` + HorizontalPodAutoscaler |

### Database migrations

The genuinely hard part, and the one most often got wrong. Schema changes must be **backwards compatible**,
because during a rolling deploy old and new code run *simultaneously*.

Renaming a column in one step breaks every old instance still running. The safe pattern is three deployments:

1. **Add** the new column; write to both, read from the old
2. **Backfill**, then switch reads to the new column
3. **Remove** the old column, once nothing references it

Slower, and the only approach that survives a rollback.

### What else production needs

| Concern | Approach |
|---------|----------|
| Secrets | Key Vault / External Secrets with **managed identity**, so there is no bootstrap credential ([ADR-0009](../adr/0009-secrets-management.md)) |
| TLS | Terminate at ingress; mTLS between services via a service mesh |
| Rollback | Redeploy the previous `sha-` tag — which is why immutable tags matter |
| Progressive delivery | Canary or blue-green, so a bad release reaches 5% of traffic rather than 100% |
| Observability | Point `OTEL_EXPORTER_OTLP_ENDPOINT` at a managed backend — one environment variable ([observability](observability.md)) |

---

## 8. Watching it run

```bash
gh run list --limit 10          # recent runs
gh run watch                    # follow the current one
gh run view --log-failed        # only the failing steps
```

Or the **Actions** tab in the browser.

---

## 9. Troubleshooting

**`denied: permission_denied` pushing to GHCR** — the workflow needs `packages: write`. It is already set in
`release.yml`; if you copy the job elsewhere, carry the permission block with it.

**Pages deploy returns 404** — Pages source is not set to "GitHub Actions". See §6.

**Multi-arch build is slow** — expected. `arm64` is emulated on x86 runners. The layer cache (`type=gha`)
makes subsequent runs much faster; the first is not.

**CI passes but `docker compose up` fails locally** — usually stale volumes. `docker compose down -v` and try
again. CI always starts clean, which is why it does not see the problem.
