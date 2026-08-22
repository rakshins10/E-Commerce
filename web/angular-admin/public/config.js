// Runtime configuration - DEVELOPMENT DEFAULTS.
//
// In a container this file is REPLACED at start by /docker-entrypoint.d/10-ecommerce-runtime-config.sh,
// which renders config.js.template from environment variables. This committed copy exists so that
// `npm run dev` - which has no container and no entrypoint - loads the same shape rather than 404ing.
//
// See docs/adr/0023. Never put a secret here: it is served to every browser.
window.__ECOMMERCE_CONFIG__ = {
  bffBaseUrl: "http://localhost:6002",
  keycloakAuthority: "http://localhost:8080/realms/ecommerce",
};
