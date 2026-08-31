namespace aspire_react.Server.Domain.Interfaces;

/// <summary>
/// Service for managing users in Keycloak via Admin REST API.
/// Synchronizes application User changes one-way to Keycloak.
/// </summary>
public interface IKeycloakService
{
    /// <summary>
    /// Ensures the "superuser" group exists in Keycloak.
    /// Called at application startup.
    /// </summary>
    Task EnsureSuperUserGroupExistsAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a user in Keycloak and returns the Keycloak user ID.
    /// </summary>
    /// <param name="username">Username (immutable in Keycloak)</param>
    /// <param name="email">Email address</param>
    /// <param name="firstName">First name</param>
    /// <param name="lastName">Last name</param>
    /// <param name="enabled">Whether the user is active/enabled</param>
    /// <returns>Keycloak user ID of the created user</returns>
    Task<string> CreateUserAsync(
        string username,
        string email,
        string firstName,
        string lastName,
        bool enabled,
        CancellationToken ct = default);

    /// <summary>
    /// Updates a user in Keycloak. Finds by username (immutable).
    /// </summary>
    Task UpdateUserAsync(
        string username,
        string email,
        string firstName,
        string lastName,
        bool enabled,
        CancellationToken ct = default);

    /// <summary>
    /// Disables a user in Keycloak (soft delete — sets enabled=false).
    /// </summary>
    Task DisableUserAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Adds a Keycloak user to the "superuser" group.
    /// </summary>
    Task AddUserToSuperUserGroupAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Removes a Keycloak user from the "superuser" group.
    /// </summary>
    Task RemoveUserFromSuperUserGroupAsync(string username, CancellationToken ct = default);
}