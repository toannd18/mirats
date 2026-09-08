var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure — Postgres password is fixed via User Secrets (secret: true, never hard-coded
// in source) so the data volume always matches the code/password across restarts (Solution 3).
var dbPassword = builder.AddParameter("dbPassword", secret: true);

// [AUTH Phase 1/5] Local JWT parameters — the ONLY auth since the Keycloak removal:
// - authSigningKey → Auth:SigningKey on the Server (HS256 key, ≥ 256 bits). NEVER committed.
// - authBootstrapPassword → Auth:BootstrapAdminPassword: one-time seeding of the local password
//   for the "admin" user when it has none yet (dev/bootstrap convenience).
var authSigningKey = builder.AddParameter("authSigningKey", secret: true);
var authBootstrapPassword = builder.AddParameter("authBootstrapPassword", secret: true);

var postgres = builder.AddPostgres("postgres", password: dbPassword)
    .WithDataVolume("postgres-data")
    .WithPgAdmin()
    .AddDatabase("aspire-react-db");

var cache = builder.AddRedis("cache");

// Backend
var server = builder.AddProject<Projects.aspire_react_Server>("server")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithReference(cache)
    .WaitFor(cache)
    .WithEnvironment("Auth__SigningKey", authSigningKey)
    .WithEnvironment("Auth__BootstrapAdminPassword", authBootstrapPassword)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

// Frontend (HTTPS dev is mandatory — see vite.config.ts + scripts/dev-server.mjs)
var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithReference(server)
    .WaitFor(server)
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = 5173;
        endpoint.IsProxied = false;
    });

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
