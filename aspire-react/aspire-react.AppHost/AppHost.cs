var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure
var postgres = builder.AddPostgres("postgres")
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
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "Admin123!");

// Backend
var server = builder.AddProject<Projects.aspire_react_Server>("server")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(keycloak)
    .WaitFor(keycloak)
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
