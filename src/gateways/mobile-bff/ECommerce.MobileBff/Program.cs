using ECommerce.Observability;

// -----------------------------------------------------------------------------
//  mobile-bff
// -----------------------------------------------------------------------------
//  Phase 1 shape: this service boots, reports health, and is observable. Its
//  domain arrives in a later phase (see the phase table in README.md).
//
//  The composition root is deliberately the ONLY place that knows about
//  infrastructure. Everything below is wiring; no business logic lives here.
//  See docs/architecture.md and docs/operations/health-checks.md.
// -----------------------------------------------------------------------------

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Structured logging, distributed tracing and metrics, configured identically in
// every service so that spans and log lines actually correlate across them.
builder.AddObservability("mobile-bff");

// Liveness is a self check only. Dependency checks (database, broker, cache) are
// registered with the `ready` tag as each service gains them, so that a database
// blip never causes the orchestrator to restart healthy processes.
builder.Services.AddDefaultHealthChecks();

// The request budget for this edge. The services behind the gateway are not limited individually -
// they accept traffic only from the internal network, and one policy at the boundary beats eleven
// copies of it. See EdgeHardening and ADR-0022.
builder.Services.AddEdgeRateLimiting(builder.Configuration);

WebApplication app = builder.Build();

// Correlation must be first: a request that fails inside exception handling
// should still be correlated. Request logging follows so its completion event
// carries the correlation id.
app.UseObservability();

// Rate limiting before auth on purpose: rejecting an abusive client must not cost a token
// validation first. Security headers on the way out for everything that survives.
app.UseRateLimiter();
app.UseSecurityHeaders();

app.MapDefaultHealthChecks();

// A minimal identity endpoint. Useful when you have thirty containers running
// and want to confirm which service is answering on a port.
app.MapGet("/", () => Results.Ok(new
{
    service = "mobile-bff",
    status = "up",
    environment = app.Environment.EnvironmentName,
}));

await app.RunAsync();
