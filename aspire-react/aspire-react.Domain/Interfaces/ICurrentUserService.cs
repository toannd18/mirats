namespace aspire_react.Server.Domain.Interfaces;

public interface ICurrentUserService
{
    /// <summary>
    /// Returns the local database user ID (Guid) from the "local_user_id" claim.
    /// This claim is set by the JIT provisioning hook in OnTokenValidated.
    /// Returns Guid.Empty if the claim is missing or invalid.
    /// </summary>
    Guid GetLocalUserId();
}