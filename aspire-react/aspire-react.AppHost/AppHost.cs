var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure — Postgres password is fixed via User Secrets (secret: true, never hard-coded
// in source) so the data volume always matches the code/password across restarts (Solution 3).
var dbPassword = builder.AddParameter("dbPassword", secret: true);

// [SECRET-ROTATE 2026-08-29] Keycloak credentials moved out of source (old values were public
// in git history). New values live in AppHost user-secrets:
//   dotnet user-secrets set "Parameters:kcBootstrapAdminPassword" "<new>" --project aspire-react.AppHost
//   dotnet user-secrets set "Parameters:kcClientSecret" "<new>"       --project aspire-react.AppHost
// - kcBootstrapAdminPassword → KC_BOOTSTRAP_ADMIN_PASSWORD (master admin, first boot only).
//   IMPORTANT: bootstrap env only seeds the H2 DB when keycloak-data volume is EMPTY; rotating
//   it later does NOT change the running master admin password (reset via Admin API instead).
// - kcClientSecret → Keycloak__ClientSecret on the Server (service-to-service auth) AND into
//   the Keycloak container (KEYCLOAK_BACKEND_CLIENT_SECRET) so a FRESH volume import resolves
//   the realm placeholder instead of importing it literally (Aspire does not substitute env in
//   realm imports — previously the running secret was the literal '${KEYCLOAK_BACKEND_CLIENT_SECRET}').
var kcBootstrapAdminPassword = builder.AddParameter("kcBootstrapAdminPassword", secret: true);
var kcClientSecret = builder.AddParameter("kcClientSecret", secret: true);

var postgres = builder.AddPostgres("postgres", password: dbPassword)
    .WithDataVolume("postgres-data")
    .WithPgAdmin()
    .AddDatabase("aspire-react-db");

var cache = builder.AddRedis("cache");

// Keycloak Authentication (persists data across restarts with volume)
// KC_BOOTSTRAP_* sets master realm admin credentials on first boot only.
// After first boot, credentials are stored in the H2 database (persisted via volume).
var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithDataVolume("keycloak-data")
    .WithRealmImport("../aspire-react-realm.json")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", kcBootstrapAdminPassword)
    .WithEnvironment("KEYCLOAK_BACKEND_CLIENT_SECRET", kcClientSecret);

// Backend
var server = builder.AddProject<Projects.aspire_react_Server>("server")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithEnvironment("Keycloak__ClientSecret", kcClientSecret)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

// Frontend (pinned to port 5173 for consistent Keycloak redirect URIs)
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
