# =============================================================================
#  Shared multi-stage Dockerfile for every web application
# =============================================================================
#  One parameterised file for all four SPAs rather than four near-identical
#  copies, for the same reason as src/Dockerfile: four copies drift, and a base
#  image bump lands in three of them.
#
#  Build context is web/ , because the apps depend on sibling workspace packages
#  (@ecommerce/shared, @ecommerce/design-tokens) and npm workspaces need the
#  root package.json and lockfile to resolve them.
# =============================================================================

ARG NODE_VERSION=22

# -----------------------------------------------------------------------------
#  Stage 1 — build
# -----------------------------------------------------------------------------
FROM node:${NODE_VERSION}-alpine AS build
WORKDIR /app

# Manifests first: this layer is cached unless a dependency actually changes,
# so a source-only edit skips the slow npm ci entirely.
COPY package.json package-lock.json ./
COPY react-store/package.json ./react-store/
COPY angular-store/package.json ./angular-store/

# `npm ci`, not `npm install`: it installs exactly the lockfile and fails if the
# lockfile and package.json disagree. `npm install` would silently resolve newer
# versions, so the image would contain something nobody tested.
RUN npm ci --no-audit --no-fund

COPY . .


ARG APP_WORKSPACE
ARG APP_DIST
RUN npm run build --workspace ${APP_WORKSPACE}

# -----------------------------------------------------------------------------
#  Stage 2 — serve
# -----------------------------------------------------------------------------
#  nginx, not `vite preview` or `ng serve`. Those are development servers: single
#  process, no caching strategy, and explicitly documented as not for production.
#  The output is static files, so a static file server is the correct tool, and
#  the runtime image contains no Node, no npm and no source.
# -----------------------------------------------------------------------------
FROM nginx:1.27-alpine AS final

ARG APP_DIST
COPY --from=build /app/${APP_DIST} /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 8080

# 127.0.0.1, NOT localhost. Inside the container `localhost` resolves to ::1
# first, and `listen 8080;` binds IPv4 only - so the probe gets "connection
# refused" while the port works perfectly from the host. A confusing failure
# worth knowing about: the container is healthy, the healthcheck is not.
HEALTHCHECK --interval=10s --timeout=3s --start-period=5s --retries=5 \
  CMD wget -qO- http://127.0.0.1:8080/healthz || exit 1

CMD ["nginx", "-g", "daemon off;"]
