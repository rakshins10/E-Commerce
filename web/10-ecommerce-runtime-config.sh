#!/bin/sh
# -----------------------------------------------------------------------------
#  Substitute this container's runtime configuration, before nginx starts.
# -----------------------------------------------------------------------------
#  Why this exists: a browser bundle cannot read environment variables, so a SPA
#  normally bakes its API addresses in at BUILD time - which means one image per
#  environment, and staging then proves nothing about production because they are
#  literally different artifacts. See docs/adr/0023.
#
#  This lives in /docker-entrypoint.d/, which the nginx image's own entrypoint
#  runs (in filename order) before it execs nginx. Using the standard hook rather
#  than replacing ENTRYPOINT keeps nginx's signal handling intact - which is what
#  makes a graceful shutdown work during a rolling update - and keeps the image's
#  other init scripts running.
#
#  TWO things are configured, and missing either one breaks the app:
#    1. config.js  - where the app SENDS requests.
#    2. the CSP in default.conf - where the browser is ALLOWED to send them.
#  Getting only the first right produces the most confusing failure in this file:
#  a correctly configured app whose every call dies in the console with "Refused
#  to connect", because the policy still names localhost.
# -----------------------------------------------------------------------------
set -eu

# --- Fail LOUDLY on a missing variable ---------------------------------------
#
# envsubst would happily substitute an empty string, and the app would start,
# render, and send every API call to a relative empty URL - a container that
# looks healthy while being completely broken. A container that refuses to start
# is diagnosable in one `kubectl logs`; one that silently misroutes is not.
missing=""
[ -n "${BFF_BASE_URL:-}" ]       || missing="$missing BFF_BASE_URL"
[ -n "${KEYCLOAK_AUTHORITY:-}" ] || missing="$missing KEYCLOAK_AUTHORITY"

if [ -n "$missing" ]; then
  echo "FATAL: required runtime configuration is missing:$missing" >&2
  echo "       Set it in the compose 'environment:' block or the Kubernetes ConfigMap." >&2
  exit 1
fi

# --- Origins, for the Content-Security-Policy --------------------------------
#
# A CSP source is an ORIGIN - scheme, host and port, no path. KEYCLOAK_AUTHORITY
# carries a realm path (.../realms/ecommerce), so trim to the origin or the whole
# policy is silently ignored by the browser as malformed.
origin_of() { echo "$1" | sed -E 's#^([A-Za-z]+://[^/]+).*#\1#'; }

BFF_ORIGIN=$(origin_of "$BFF_BASE_URL")
KEYCLOAK_ORIGIN=$(origin_of "$KEYCLOAK_AUTHORITY")
export BFF_ORIGIN KEYCLOAK_ORIGIN

# --- Substitute --------------------------------------------------------------
#
# Naming each variable, rather than bare `envsubst`, matters more in the nginx
# template than in the JavaScript one: nginx configuration is FULL of $variables
# ($uri, $host, $remote_addr) and bare envsubst would blank every one of them
# that happens to share a name with an environment variable.
envsubst '${BFF_BASE_URL} ${KEYCLOAK_AUTHORITY}' \
  < /usr/share/nginx/html/config.js.template \
  > /usr/share/nginx/html/config.js

envsubst '${BFF_ORIGIN} ${KEYCLOAK_ORIGIN}' \
  < /etc/nginx/ecommerce/default.conf.template \
  > /etc/nginx/conf.d/default.conf

echo "runtime config: bff=$BFF_BASE_URL keycloak=$KEYCLOAK_AUTHORITY"
echo "runtime config: csp connect-src 'self' $BFF_ORIGIN $KEYCLOAK_ORIGIN"
