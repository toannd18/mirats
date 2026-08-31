namespace aspire_react.Server.Domain.Interfaces;

/// <summary>
/// [Giai đoạn 0.1 — F1] Moved verbatim from Infrastructure/Services/IAssetTagGenerator.cs so that
/// Application (CreateAssetCommand) can depend on the contract WITHOUT referencing Infrastructure.
/// Implementation (AssetTagGenerator) stays in Infrastructure. Content unchanged.
/// </summary>
public interface IAssetTagGenerator
{
    /// <summary>
    /// Returns the tag to use for a new asset: <paramref name="explicitTag"/> if non-empty, otherwise a
    /// newly generated tag from the configured format. The counter is keyed by (<paramref name="companyId"/>, year)
    /// and updated under a transaction + FOR UPDATE so concurrent creation never collides.
    /// </summary>
    Task<string> ResolveAssetTagAsync(string? explicitTag, Guid? companyId, CancellationToken ct = default);

    /// <summary>Gets the current configured format template (default "AST-{YYYY}-{SEQ:3}" if unset).</summary>
    Task<string> GetFormatAsync(CancellationToken ct = default);

    /// <summary>Sets the configured format template.</summary>
    Task SetFormatAsync(string format, Guid? updatedBy, CancellationToken ct = default);
}
