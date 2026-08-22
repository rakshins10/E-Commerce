# =============================================================================
#  Shared multi-stage Dockerfile for every web application
# =============================================================================
#  One parameterised file for all four SPAs rather than four near-identical
#  copies, for the same reason as src/Dockerfile: four copies drift, and a base
#  image bump lands in three of them.
#
#  Build context is web/ , because the apps depend on sibling workspace packages
#  npm workspaces need every workspace's package.json present before install, so
#  root package.json and lockfile to resolve them.
# =============================================================================

ARG NODE_VERSION=22

# -----------------------------------------------------------------------------
#  Stage 1 - build
# -----------------------------------------------------------------------------
FROM node:${NODE_VERSION}-alpine AS build
WORKDIR /app

# Manifests first: this layer is cached unless a dependency actually changes,
# so a source-only edit skips the slow npm ci entirely.
COPY package.json package-lock.json ./
COPY react-store/package.json ./react-store/
COPY angular-store/package.json ./angular-store/
COPY react-admin/package.json ./react-admin/
COPY angular-admin/package.json ./angular-admin/

# `npm ci`, not `npm install`: it installs exactly the lockfile and fails if the
# lockfile and package.json disagree. `npm install` would silently resolve newer
# versions, so the image would contain something nobody tested.
RUN npm ci --no-audit --no-fund

COPY . .


ARG APP_WORKSPACE
ARG APP_DIST
RUN npm run build --workspace ${APP_WORKSPACE}

# -----------------------------------------------------------------------------
#  Stage 2 - serve
# -----------------------------------------------------------------------------
#  nginx, not `vite preview` or `ng serve`. Those are development servers: single
#  process, no caching strategy, and explicitly documented as not for production.
#  The output is static files, so a static file server is the correct tool, and
#  the runtime image contains no Node, no npm and no source.
# -----------------------------------------------------------------------------
FROM nginx:1.27-alpine AS final

ARG APP_DIST
COPY --from=build /app/${APP_DIST} /usr/share/nginx/html
# Runtime configuration (docs/adr/0023). The image is built ONCE and told where the BFF and Keycloak
# live when it starts, so the artifact that passed staging is the artifact that reaches production.
# Both the app's config.js and the nginx CSP are substituted from the same two variables.
#
# envsubst comes from gettext, which the nginx alpine image does not ship. There is no default.conf in
# conf.d until the entrypoint writes one - deliberately: an unconfigured container must not serve.
RUN apk add --no-cache gettext
COPY nginx.conf.template /etc/nginx/ecommerce/default.conf.template
COPY config.js.template /usr/share/nginx/html/config.js.template
COPY 10-ecommerce-runtime-config.sh /docker-entrypoint.d/10-ecommerce-runtime-config.sh
RUN chmod +x /docker-entrypoint.d/10-ecommerce-runtime-config.sh

EXPOSE 8080

# 127.0.0.1, NOT localhost. Inside the container `localhost` resolves to ::1
# first, and `listen 8080;` binds IPv4 only - so the probe gets "connection
# refused" while the port works perfectly from the host. A confusing failure
# worth knowing about: the container is healthy, the healthcheck is not.
HEALTHCHECK --interval=10s --timeout=3s --start-period=5s --retries=5 \
  CMD wget -qO- http://127.0.0.1:8080/healthz || exit 1

# No ENTRYPOINT override: the nginx image's own entrypoint runs /docker-entrypoint.d/* and then execs
# nginx as PID 1, so SIGTERM reaches nginx directly. That is what lets Kubernetes drain connections
# gracefully during a rolling update instead of killing them (docs/kubernetes/rolling-updates.md).
CMD ["nginx", "-g", "daemon off;"]
