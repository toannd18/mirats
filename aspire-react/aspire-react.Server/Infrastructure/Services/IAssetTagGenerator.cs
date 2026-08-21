namespace aspire_react.Server.Infrastructure.Services;

/// <summary>
/// Generates Asset Tags from an admin-configured format template (Task ASSET-TAG-AUTO).
/// When the caller supplies an explicit tag it is used as-is; when empty/null the tag is generated.
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
