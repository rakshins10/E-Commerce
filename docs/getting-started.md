# Getting started

> **Related:** [Architecture](architecture.md) · [Documentation index](README.md) ·
> [Runbook](operations/runbook.md)

Written for a developer on a clean machine with nothing installed. If any step here does not work as written,
that is a bug — the last phase of this project re-runs this page on a fresh machine specifically to check.

---

## 1. What you actually need

**To run the whole system, you need Docker and nothing else.** Everything — services, databases, Keycloak,
the broker, the observability stack — builds and runs in containers. The .NET SDK and Node are needed only if
you want to run a component *outside* its container while developing it.

| Tool | Version | Needed for | Check |
|------|---------|-----------|-------|
| **Docker Desktop** | 24+ with Compose v2 | Everything | `docker --version && docker compose version` |
| Git | 2.40+ | Cloning | `git --version` |
| .NET SDK | **10.0.3xx** | Building services outside Docker | `dotnet --version` |
| Node.js | 22 LTS or 24 | Frontends outside Docker | `node --version` |
| `dotnet-ef` | matching the SDK | Creating and applying migrations | `dotnet ef --version` |

### Docker Desktop resources

**Allocate at least 8 GB of RAM and 4 CPUs**, in Settings → Resources. The stack runs ~27 containers. Below
8 GB, containers are killed by the OOM reaper mid-startup, which surfaces as services flapping between
starting and unhealthy rather than as an obvious out-of-memory message — a genuinely confusing failure.

Disk: allow ~15 GB for images and volumes.

### Installing the .NET SDK (only if building outside Docker)

```powershell
# Windows, machine-wide — needs an elevated terminal
winget install Microsoft.DotNet.SDK.10
```

If you cannot elevate, the official script installs per-user with no admin rights:

```powershell
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
./dotnet-install.ps1 -Channel 10.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"
```

A per-user install is **not on `PATH` by default**. Either add it, or invoke it by full path. Note that if
you put a per-user .NET 10 root ahead of a machine-wide .NET 9 on `PATH`, the .NET 9 *runtime* is no longer
visible to it — so other projects targeting `net9.0` may fail to run. The machine-wide install avoids this.

```bash
# Linux / macOS
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
```

`global.json` pins the SDK feature band, so a mismatched SDK fails immediately with a clear message rather
than building subtly differently.

---

## 2. First run

```bash
git clone https://github.com/rakshins10/E-Commerce.git
cd E-Commerce/deploy
cp .env.example .env
docker compose up -d --build
```

### What to expect

| Stage | Time | What is happening |
|-------|------|-------------------|
| Image pull | 3–10 min | Postgres, Keycloak, RabbitMQ, Mongo, Redis, Seq, Jaeger, .NET base images |
| .NET builds | 5–15 min | Nine services and three gateways compiled. **First build only** — later builds reuse cached layers |
| Startup | 60–90 s | Databases initialise, Keycloak imports the realm, services connect |

**Total on a first run: 10–25 minutes.** Subsequent `docker compose up -d` takes about 60 seconds.

Wait for health rather than guessing:

```bash
docker compose up -d --wait --wait-timeout 600
```

`--wait` blocks until every service with a health check reports healthy, and **fails if one starts and then
crashes** — which a plain `up -d` reports as success.

---

## 3. Verify

```bash
docker compose ps
```

Every row should read `Up` and, where a health check exists, `(healthy)`. Then:

```bash
# Each service answers with its own name
curl http://localhost:5001/          # catalog
curl http://localhost:5001/health/live
curl http://localhost:5001/health/ready
```

### Everything you can open

| Surface | URL | Notes |
|---------|-----|-------|
| React storefront | http://localhost:3000 | Browse, buy, track orders |
| React admin | http://localhost:3001 | Dashboard, catalogue, orders, stock, users |
| Angular storefront | http://localhost:4200 | Functionally identical to :3000 |
| Angular admin | http://localhost:4201 | Functionally identical to :3001 |
| Storefront BFF | http://localhost:6001 | |
| Admin BFF | http://localhost:6002 | |
| Mobile BFF | http://localhost:6003 | |
| **Keycloak admin console** | http://localhost:8080 | `admin` / `dev_only_kc_admin_pw` |
| **RabbitMQ management** | http://localhost:15672 | `ecom` / `dev_only_rabbit_pw` — watch queue depth and dead letters |
| **Seq (logs)** | http://localhost:8081 | Filter by `CorrelationId` to follow one request |
| **Jaeger (traces)** | http://localhost:16686 | Follow one order across every service |

Service HTTP ports are `5001`–`5009`; gRPC is the HTTP port **+100** (`5101`–`5107`). Per-service Postgres
instances are on `15432`–`15440`. The full allocation is in
[`deploy/.env.example`](../deploy/.env.example) and [architecture.md §9](architecture.md#9-port-allocation).

> **New to Seq, Jaeger, RabbitMQ or Keycloak?** Read
> [**operations/tooling-guide.md**](operations/tooling-guide.md) — a hands-on introduction to all four
> assuming no prior knowledge, with exercises you can run against the stack you just started.

---

## 4. Everyday commands

```bash
docker compose logs -f catalog-api        # follow one service
docker compose restart catalog-api        # restart one service
docker compose up -d --build catalog-api  # rebuild one service after a code change
docker compose down                       # stop, keep data
docker compose down -v                    # stop and DELETE ALL DATA (full reset)
docker compose ps                         # what is running and healthy
```

### Running a service outside Docker

Useful for debugging with a breakpoint. Leave the infrastructure in containers and run just the one service:

```bash
cd deploy && docker compose up -d catalog-db rabbitmq keycloak seq jaeger
cd ../src/services/catalog/ECommerce.Catalog.Api
dotnet run
```

Because the service now runs on the host, its dependencies are on `localhost` at their *host* ports, not at
their compose service names. Override them with user-secrets rather than editing `appsettings.json` — that
file is tracked, and a machine-specific connection string in it will be committed by someone eventually:

```bash
dotnet user-secrets set "ConnectionStrings:CatalogDb" "Host=localhost;Port=15433;Database=catalog;Username=ecom;Password=dev_only_pg_pw"
```

### Tests

```bash
dotnet test ECommerce.slnx                       # unit + integration
dotnet test tests/unit/ECommerce.Architecture.Tests   # boundary rules only
```

Integration tests use **Testcontainers**, which starts real Postgres, RabbitMQ, and Keycloak containers per
test class. Docker must be running; no other setup is required, and nothing is left behind.

### Migrations

```bash
dotnet ef migrations add <Name> \
  --project src/services/catalog/ECommerce.Catalog.Infrastructure \
  --startup-project src/services/catalog/ECommerce.Catalog.Api
```

Each service owns its own migration history in its own database. There is no solution-wide migration step, and
that is the point — see [data sovereignty](architecture.md#7-data-sovereignty-why-services-never-share-a-database).

---

## 5. Troubleshooting

These are the failures that actually happen, not hypothetical ones.

### Keycloak: every request returns 401 and the log says the issuer is invalid

**The single most common problem in this stack.** Inside the compose network Keycloak is
`http://keycloak:8080`; to your browser it is `http://localhost:8080`. A token minted for a browser carries
`iss: http://localhost:8080/...`, so a service validating against the internal hostname rejects every token —
even though both are the same server.

The fix is already in the compose file: `KC_HOSTNAME` pins one public URL, and services validate against
`Auth__Issuer` set to that same value while fetching metadata over the internal address. If you change
`PUBLIC_HOST` or the Keycloak port, **change both** or this returns.

### Keycloak is not ready and dependent services fail on startup

Keycloak takes 30–60 seconds to import the realm. Services depend on it with
`condition: service_healthy` rather than a fixed sleep, so this should self-resolve. If it does not:

```bash
docker compose logs keycloak | tail -50
```

A realm import failure is usually malformed `realm-export.json`, and the log names the offending element.

### Port already in use

```bash
docker compose down                       # a previous stack still running?
netstat -ano | findstr :5001              # Windows — find the process
lsof -i :5001                             # macOS / Linux
```

Every port is an environment variable. Change it in `.env` rather than editing `docker-compose.yml`:

```dotenv
CATALOG_HTTP_PORT=15001
```

### CORS errors in the browser

The SPAs call their BFF, which is a different origin. CORS is configured on the BFF, and the allowed origins
come from configuration. If you changed a frontend port in `.env`, the BFF's allowed-origin list must match —
mismatched ports are the usual cause, not a missing header.

### A service is `unhealthy` and the logs mention a connection refused

Almost always a dependency that has not finished starting. `docker compose ps` shows what is not yet healthy.
If a database is stuck in `starting`, its volume may be corrupt from an interrupted first run:

```bash
docker compose down -v && docker compose up -d --wait   # deletes all data
```

### Containers are killed during startup, or repeatedly restart

Docker Desktop memory. See §1 — raise it to 8 GB. The symptom is services flapping rather than an explicit
out-of-memory error, which makes this much harder to spot than it should be.

### `dotnet build` fails with an SDK version error

`global.json` pins the SDK feature band. Either install .NET 10 (§1), or check that the `dotnet` on your
`PATH` is the one you think it is — a per-user install does not shadow a machine-wide one unless you put it
on `PATH` first.

### Docker build fails pulling a base image (TLS handshake timeout)

Transient registry trouble, not a configuration problem. Re-run the command; Docker resumes from the layers
it already has.

---

## 6. A clean reset

```bash
cd deploy
docker compose down -v          # containers and volumes
docker system prune -af         # images and build cache — frees a lot, costs a slow next build
```
