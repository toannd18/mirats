namespace aspire_react.Server.Domain.Exceptions;

/// <summary>
/// [Giai đoạn 0.1 — F1 pattern #6] Moved verbatim from Infrastructure/Services/KeycloakService.cs
/// (bottom of file) — this is a CROSS-LAYER contract exception: thrown by KeycloakService /
/// JitUserProvisioningService (Infrastructure) and TEST fake, caught by CreateUser/UpdateUser
/// commands (Application). Application must see the type without referencing Infrastructure, so
/// the contract lives in Domain. Content unchanged.
/// </summary>
public class KeycloakApiException : Exception
{
    public string? ErrorCode { get; }

    public KeycloakApiException(string message, string? errorCode = null)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}
