using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using aspire_react.Server.Domain.Exceptions;
using aspire_react.Server.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace aspire_react.Server.Infrastructure.Services;

/// <summary>
/// Options for Keycloak Admin REST API connection.
/// Configured via appsettings.json or environment variables.
/// </summary>
public class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>Keycloak server base URL (e.g. https://localhost:8080)</summary>
    public string ServerUrl { get; set; } = "https://localhost:8080";

    /// <summary>Realm name (e.g. aspire-react)</summary>
    public string Realm { get; set; } = "aspire-react";

    /// <summary>
    /// Confidential client ID with service account enabled and realm-management roles.
    /// Used for client_credentials grant (never password grant — no credentials in config).
    /// </summary>
    public string ClientId { get; set; } = "backend-service";

    /// <summary>
    /// Client secret for client_credentials grant.
    /// Store securely via environment variables or secrets manager — never hardcode in source.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Name of the superuser group in Keycloak</summary>
    public string SuperUserGroupName { get; set; } = "superuser";

    /// <summary>Timeout in seconds for Keycloak API calls</summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Implementation of IKeycloakService using Keycloak Admin REST API.
/// Handles one-way sync from application to Keycloak (create/update/disable users, manage groups).
/// </summary>
public class KeycloakService : IKeycloakService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly KeycloakOptions _options;
    private readonly ILogger<KeycloakService> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _adminToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public KeycloakService(
        IHttpClientFactory httpClientFactory,
        IOptions<KeycloakOptions> options,
        ILogger<KeycloakService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("KeycloakAdmin");
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        return client;
    }

    /// <summary>Base admin API URL for the configured realm.</summary>
    private string AdminBaseUrl => $"{_options.ServerUrl.TrimEnd('/')}/admin/realms/{_options.Realm}";

    /// <inheritdoc/>
    public async Task EnsureSuperUserGroupExistsAsync(CancellationToken ct = default)
    {
        var token = await GetAdminTokenAsync(ct);
        var groupId = await GetGroupIdByNameAsync(_options.SuperUserGroupName, token, ct);

        if (groupId != null)
        {
            _logger.LogInformation("Keycloak group '{GroupName}' already exists (id: {GroupId}).",
                _options.SuperUserGroupName, groupId);
            return;
        }

        _logger.LogInformation("Keycloak group '{GroupName}' not found. Creating...",
            _options.SuperUserGroupName);

        var createPayload = new { name = _options.SuperUserGroupName };
        var content = new StringContent(
            JsonSerializer.Serialize(createPayload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{AdminBaseUrl}/groups")
        {
            Content = content,
        };
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var httpClient = CreateClient();
        var response = await httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            // Extract group ID from Location header or re-query
            var location = response.Headers.Location?.ToString();
            _logger.LogInformation("Keycloak group '{GroupName}' created successfully. Location: {Location}",
                _options.SuperUserGroupName, location);
        }
        else
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Failed to create Keycloak group '{GroupName}'. Status: {Status}, Body: {Body}",
                _options.SuperUserGroupName, response.StatusCode, errorBody);

            throw new KeycloakApiException(
                $"Failed to create group '{_options.SuperUserGroupName}' in Keycloak. " +
                $"Status: {response.StatusCode}, Details: {errorBody}");
        }
    }

    /// <inheritdoc/>
    public async Task<string> CreateUserAsync(
        string username,
        string email,
        string firstName,
        string lastName,
        bool enabled,
        CancellationToken ct = default)
    {
        ValidateKeycloakInput(username, email, firstName, lastName);

        var token = await GetAdminTokenAsync(ct);

        // Check if user already exists by username
        var existingId = await GetUserIdByUsernameAsync(username, token, ct);
        if (existingId != null)
        {
            throw new KeycloakApiException(
                $"Username '{username}' already exists in Keycloak. Please choose a different username.",
                "KEYCLOAK_USERNAME_EXISTS");
        }

        // Check if email already exists
        var existingByEmail = await SearchUsersByEmailAsync(email, token, ct);
        if (existingByEmail is { Count: > 0 })
        {
            throw new KeycloakApiException(
                $"Email '{email}' is already registered in Keycloak. Please use a different email.",
                "KEYCLOAK_EMAIL_EXISTS");
        }

        var payload = new
        {
            username,
            email,
            firstName,
            lastName,
            enabled,
            emailVerified = false,
            requiredActions = new string[] { "UPDATE_PASSWORD" },
            credentials = new[]
            {
                new
                {
                    type = "password",
                    value = GenerateTemporaryPassword(),
                    temporary = true,
                },
            },
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{AdminBaseUrl}/users")
        {
            Content = content,
        };
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var httpClient = CreateClient();
        var response = await httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new KeycloakApiException(
                $"Failed to create user '{username}' in Keycloak. Status: {response.StatusCode}, Details: {errorBody}",
                "KEYCLOAK_CREATE_FAILED");
        }

        // Extract user ID from Location header, then re-query to get the ID
        var location = response.Headers.Location?.ToString();
        var createdUserId = await GetUserIdByUsernameAsync(username, token, ct);

        if (string.IsNullOrEmpty(createdUserId))
        {
            throw new KeycloakApiException(
                $"User '{username}' was created in Keycloak but could not retrieve the user ID.",
                "KEYCLOAK_ID_RETRIEVAL_FAILED");
        }

        _logger.LogInformation("User '{Username}' created in Keycloak with ID: {UserId}",
            username, createdUserId);

        return createdUserId;
    }

    /// <inheritdoc/>
    public async Task UpdateUserAsync(
        string username,
        string email,
        string firstName,
        string lastName,
        bool enabled,
        CancellationToken ct = default)
    {
        ValidateKeycloakInput(username, email, firstName, lastName);

        var token = await GetAdminTokenAsync(ct);

        var userId = await GetUserIdByUsernameAsync(username, token, ct);
        if (userId == null)
        {
            throw new KeycloakApiException(
                $"User '{username}' not found in Keycloak. Cannot update.",
                "KEYCLOAK_USER_NOT_FOUND");
        }

        var payload = new
        {
            email,
            firstName,
            lastName,
            enabled,
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var request = new HttpRequestMessage(HttpMethod.Put, $"{AdminBaseUrl}/users/{userId}")
        {
            Content = content,
        };
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var httpClient = CreateClient();
        var response = await httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new KeycloakApiException(
                $"Failed to update user '{username}' in Keycloak. Status: {response.StatusCode}, Details: {errorBody}",
                "KEYCLOAK_UPDATE_FAILED");
        }

        _logger.LogInformation("User '{Username}' updated in Keycloak.", username);
    }

    /// <inheritdoc/>
    public async Task DisableUserAsync(string username, CancellationToken ct = default)
    {
        var token = await GetAdminTokenAsync(ct);

        var userId = await GetUserIdByUsernameAsync(username, token, ct);
        if (userId == null)
        {
            _logger.LogWarning("User '{Username}' not found in Keycloak. Skipping disable.", username);
            return;
        }

        var payload = new { enabled = false };
        var content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"{AdminBaseUrl}/users/{userId}")
        {
            Content = content,
        };
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var httpClient = CreateClient();
        var response = await httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Failed to disable user '{Username}' in Keycloak. Status: {Status}, Body: {Body}",
                username, response.StatusCode, errorBody);
            // Do not throw — disabling is a soft action, local DB takes precedence
        }
        else
        {
            _logger.LogInformation("User '{Username}' disabled in Keycloak.", username);
        }
    }

    /// <inheritdoc/>
    public async Task AddUserToSuperUserGroupAsync(string username, CancellationToken ct = default)
    {
        var token = await GetAdminTokenAsync(ct);

        var userId = await GetUserIdByUsernameAsync(username, token, ct);
        if (userId == null)
        {
            throw new KeycloakApiException(
                $"User '{username}' not found in Keycloak for group assignment.",
                "KEYCLOAK_USER_NOT_FOUND");
        }

        var groupId = await GetGroupIdByNameAsync(_options.SuperUserGroupName, token, ct);
        if (groupId == null)
        {
            throw new KeycloakApiException(
                $"Group '{_options.SuperUserGroupName}' not found in Keycloak.",
                "KEYCLOAK_GROUP_NOT_FOUND");
        }

        var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"{AdminBaseUrl}/users/{userId}/groups/{groupId}");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var httpClient = CreateClient();
        var response = await httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new KeycloakApiException(
                $"Failed to add user '{username}' to group '{_options.SuperUserGroupName}'. " +
                $"Status: {response.StatusCode}, Details: {errorBody}",
                "KEYCLOAK_GROUP_ASSIGN_FAILED");
        }

        _logger.LogInformation("User '{Username}' added to Keycloak group '{GroupName}'.",
            username, _options.SuperUserGroupName);
    }

    /// <inheritdoc/>
    public async Task RemoveUserFromSuperUserGroupAsync(string username, CancellationToken ct = default)
    {
        var token = await GetAdminTokenAsync(ct);

        var userId = await GetUserIdByUsernameAsync(username, token, ct);
        if (userId == null)
        {
            _logger.LogWarning(
                "User '{Username}' not found in Keycloak. Skipping group removal.", username);
            return;
        }

        var groupId = await GetGroupIdByNameAsync(_options.SuperUserGroupName, token, ct);
        if (groupId == null)
        {
            _logger.LogWarning(
                "Group '{GroupName}' not found in Keycloak. Skipping group removal.",
                _options.SuperUserGroupName);
            return;
        }

        var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"{AdminBaseUrl}/users/{userId}/groups/{groupId}");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var httpClient = CreateClient();
        var response = await httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Failed to remove user '{Username}' from group '{GroupName}'. Status: {Status}, Body: {Body}",
                username, _options.SuperUserGroupName, response.StatusCode, errorBody);
            // Do not throw — local DB takes precedence
        }
        else
        {
            _logger.LogInformation(
                "User '{Username}' removed from Keycloak group '{GroupName}'.",
                username, _options.SuperUserGroupName);
        }
    }

    // ==================== Private Helpers ====================

    /// <summary>
    /// Obtains an admin access token from Keycloak using client credentials or password grant.
    /// Caches the token until it expires.
    /// </summary>
    private async Task<string> GetAdminTokenAsync(CancellationToken ct)
    {
        if (_adminToken != null && DateTime.UtcNow < _tokenExpiry.AddSeconds(-30))
        {
            return _adminToken;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_adminToken != null && DateTime.UtcNow < _tokenExpiry.AddSeconds(-30))
            {
                return _adminToken;
            }

            // Token endpoint: client_credentials grant against the configured realm.
            // Uses a confidential service-account client (backend-service) with realm-management roles.
            // No username/password ever stored in configuration — secure by design.
            var tokenEndpoint = $"{_options.ServerUrl.TrimEnd('/')}/realms/{_options.Realm}/protocol/openid-connect/token";

            var formParams = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
            };

            var formContent = new FormUrlEncodedContent(formParams);

            var httpClient = CreateClient();
            var response = await httpClient.PostAsync(tokenEndpoint, formContent, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw new KeycloakApiException(
                    $"Failed to obtain Keycloak admin token. Status: {response.StatusCode}, Body: {errorBody}",
                    "KEYCLOAK_AUTH_FAILED");
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<KeycloakTokenResponse>(
                JsonOptions, ct);

            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                throw new KeycloakApiException(
                    "Keycloak token response is null or missing access_token.",
                    "KEYCLOAK_AUTH_FAILED");
            }

            _adminToken = tokenResponse.AccessToken;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn > 0
                ? tokenResponse.ExpiresIn
                : 300);

            _logger.LogDebug("Obtained new Keycloak admin token, expires in {ExpiresIn}s.",
                tokenResponse.ExpiresIn);

            return _adminToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>Searches for a user by exact username. Returns user ID or null.</summary>
    private async Task<string?> GetUserIdByUsernameAsync(
        string username,
        string token,
        CancellationToken ct)
    {
        var url = $"{AdminBaseUrl}/users?username={Uri.EscapeDataString(username)}&exact=true&briefRepresentation=true";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var httpClient = CreateClient();
        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;

        var users = await response.Content.ReadFromJsonAsync<List<KeycloakBriefUser>>(
            JsonOptions, ct);

        return users?.FirstOrDefault()?.Id;
    }

    /// <summary>Searches users by exact email. Returns list of matching users.</summary>
    private async Task<List<KeycloakBriefUser>?> SearchUsersByEmailAsync(
        string email,
        string token,
        CancellationToken ct)
    {
        var url = $"{AdminBaseUrl}/users?email={Uri.EscapeDataString(email)}&exact=true&briefRepresentation=true";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var httpClient = CreateClient();
        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadFromJsonAsync<List<KeycloakBriefUser>>(
            JsonOptions, ct);
    }

    /// <summary>Finds a group by name; returns its ID or null.</summary>
    private async Task<string?> GetGroupIdByNameAsync(
        string groupName,
        string token,
        CancellationToken ct)
    {
        var url = $"{AdminBaseUrl}/groups?search={Uri.EscapeDataString(groupName)}&exact=true&briefRepresentation=true";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var httpClient = CreateClient();
        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;

        var groups = await response.Content.ReadFromJsonAsync<List<KeycloakBriefGroup>>(
            JsonOptions, ct);

        return groups?.FirstOrDefault(g =>
            string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    /// <summary>Validates required input fields for Keycloak operations.</summary>
    private static void ValidateKeycloakInput(
        string username,
        string email,
        string firstName,
        string lastName)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username is required.", nameof(username));

        if (username.Contains(' '))
            throw new ArgumentException("Username must not contain spaces.", nameof(username));

        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));

        if (string.IsNullOrWhiteSpace(firstName))
            throw new ArgumentException("First name is required.", nameof(firstName));

        if (string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("Last name is required.", nameof(lastName));
    }

    /// <summary>Generates a temporary password for new Keycloak users (requires change on first login).</summary>
    private static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghjkmnpqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!@#$%^&*";
        var sb = new StringBuilder(16);
        var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        var bytes = new byte[16];

        rng.GetBytes(bytes);
        sb.Append(upper[bytes[0] % upper.Length]);
        sb.Append(lower[bytes[1] % lower.Length]);
        sb.Append(digits[bytes[2] % digits.Length]);
        sb.Append(special[bytes[3] % special.Length]);

        for (int i = 4; i < 16; i++)
        {
            var all = upper + lower + digits + special;
            sb.Append(all[bytes[i] % all.Length]);
        }

        // Shuffle
        var arr = sb.ToString().ToCharArray();
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = bytes[i % bytes.Length] % (i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }

        return new string(arr);
    }

    // ==================== Helper DTOs ====================

    private class KeycloakTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;
    }

    private class KeycloakBriefUser
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;
    }

    private class KeycloakBriefGroup
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}

// [Giai đoạn 0.1 — F1 pattern #6] KeycloakApiException moved verbatim to
// Domain/Exceptions/KeycloakApiException.cs — cross-layer contract exception (thrown here,
// caught by Application User commands); Application must not reference Infrastructure.