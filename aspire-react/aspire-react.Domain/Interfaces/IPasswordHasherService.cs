namespace aspire_react.Server.Domain.Interfaces;

/// <summary>
/// [AUTH Phase 1] Password hashing contract — thin wrapper over the framework PBKDF2 hasher so
/// Application-layer handlers never touch framework types (implementation in
/// Infrastructure/Authentication; same dependency-direction rule as ICompanyScopeService).
/// </summary>
public interface IPasswordHasherService
{
    /// <summary>Hashes a plaintext password for storage. Never store or log the plaintext.</summary>
    string Hash(string password);

    /// <summary>
    /// Verifies a plaintext password against a stored hash. RehashNeeded lets the handler
    /// transparently upgrade legacy parameter settings on successful login.
    /// </summary>
    PasswordVerifyResult Verify(string password, string storedHash);
}

/// <summary>Outcome of a password verification.</summary>
public enum PasswordVerifyResult
{
    /// <summary>Password matches the stored hash.</summary>
    Success,

    /// <summary>Password matches but the stored hash used outdated parameters — rehash and persist.</summary>
    RehashNeeded,

    /// <summary>Password does not match.</summary>
    Failed
}
