using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace aspire_react.Server.Infrastructure.Authentication;

/// <summary>
/// [AUTH Phase 1] PBKDF2 password hashing via the framework PasswordHasher (ASP.NET Core Identity
/// core primitives — NOT the full membership stack). Wrapped behind IPasswordHasherService so
/// Application-layer handlers never touch framework types (dependency direction — see
/// AUTH_MIGRATION_PLAYBOOK §11.1).
/// </summary>
public class PasswordHasherService : IPasswordHasherService
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password)
        => _hasher.HashPassword(null!, password);

    public PasswordVerifyResult Verify(string password, string storedHash)
    {
        var result = _hasher.VerifyHashedPassword(null!, storedHash, password);
        return result switch
        {
            PasswordVerificationResult.Success => PasswordVerifyResult.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerifyResult.RehashNeeded,
            _ => PasswordVerifyResult.Failed
        };
    }
}
