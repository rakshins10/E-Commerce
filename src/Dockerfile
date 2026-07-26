# =============================================================================
#  Shared multi-stage Dockerfile for every .NET service and gateway.
# =============================================================================
#  One parameterised file rather than twelve near-identical ones. Twelve copies
#  drift: a base-image bump lands in eleven of them and the twelfth quietly runs
#  an unpatched runtime for six months. Each service supplies PROJECT_PATH and
#  ASSEMBLY_NAME from docker-compose.yml.
#
#  Build context is the REPOSITORY ROOT, not the service directory, because the
#  build needs global.json, Directory.Build.props and Directory.Packages.props
#  (central package management means a project cannot restore without them).
#
#  See docs/getting-started.md.
# =============================================================================

ARG DOTNET_VERSION=10.0

# -----------------------------------------------------------------------------
#  Stage 1 — restore
# -----------------------------------------------------------------------------
#  Split from the build stage so that a source-only change reuses the cached
#  restore layer. Restore is the slow part; source changes are the frequent part.
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS restore
WORKDIR /src

# Solution-level files first: they change rarely, so this layer is cached across
# nearly every build.
COPY global.json Directory.Build.props Directory.Packages.props ./

# Then the project files. Docker cannot glob into nested paths, so the whole src
# tree is copied and everything but the project graph is discarded. This is less
# elegant than listing all 22 .csproj paths explicitly, but it does not need
# editing every time a project is added -- and a stale hand-written list produces
# a confusing "project not found" at restore time.
COPY src/ src/
RUN find src -name '*.cs' -delete && find src -name 'appsettings*.json' -delete

ARG PROJECT_PATH
RUN dotnet restore "${PROJECT_PATH}"

# -----------------------------------------------------------------------------
#  Stage 2 — publish
# -----------------------------------------------------------------------------
FROM restore AS build
WORKDIR /src
COPY src/ src/

ARG PROJECT_PATH
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "${PROJECT_PATH}" \
    -c "${BUILD_CONFIGURATION}" \
    --no-restore \
    -o /app/publish

# -----------------------------------------------------------------------------
#  Stage 3 — runtime
# -----------------------------------------------------------------------------
#  The aspnet image, not the sdk image: ~110 MB instead of ~850 MB, and it
#  contains no compiler, no NuGet cache and no source. Smaller is faster to pull,
#  but the real argument is security -- a runtime image has a far smaller attack
#  surface, and an attacker who reaches the container finds no build tooling.
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
WORKDIR /app

# curl is added purely so `docker compose` healthchecks can probe /health/live.
# The .NET runtime images ship without curl or wget by design, and a healthcheck
# is worth ~2 MB: without one, `depends_on: condition: service_healthy` cannot
# work and services start against dependencies that are not yet accepting
# connections. In Kubernetes this would not be needed -- the kubelet performs
# HTTP probes itself, with no in-container client.
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Run as an unprivileged user. The .NET images ship a non-root `app` user (UID
# 1654) for exactly this. Root in a container is not a sandbox: combined with a
# kernel or runtime escape it is root on the host.
USER app

# Kestrel listens on 8080 for HTTP and 8081 for gRPC. Two ports because Kestrel
# cannot multiplex HTTP/1.1 and HTTP/2 on one *plaintext* port, and TLS between
# containers is out of scope here. Behind TLS in production both share 443 --
# this is an artefact of the local setup, not of gRPC.
# See docs/adr/0007-grpc-for-internal-sync-calls.md.
EXPOSE 8080
EXPOSE 8081

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

COPY --from=build /app/publish .

# ASSEMBLY_NAME is baked into the entrypoint via an env var because ENTRYPOINT
# cannot expand a build ARG directly.
ARG ASSEMBLY_NAME
ENV ASSEMBLY_NAME=${ASSEMBLY_NAME}

# Exec form (no shell), so the .NET process is PID 1 and receives SIGTERM
# directly. With the shell form, the shell is PID 1 and swallows the signal, so
# the container is SIGKILLed after the grace period and in-flight requests and
# unacknowledged messages are lost. This one line is the difference between
# graceful and abrupt shutdown.
ENTRYPOINT ["sh", "-c", "exec dotnet /app/${ASSEMBLY_NAME}.dll"]
