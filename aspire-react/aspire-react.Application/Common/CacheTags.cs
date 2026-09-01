namespace aspire_react.Server.Application.Common;

/// <summary>
/// [Giai đoạn 1.5] Output-cache tags for the reference-data groups — moved verbatim from
/// Infrastructure/Caching/CacheInvalidator.cs so that Application-layer commands can reference
/// the constants in <see cref="ICacheInvalidatingCommand{TResponse}.CacheTagsToInvalidate"/>
/// (Application must not reference Infrastructure). Values MUST keep matching the
/// <c>[OutputCache(Tags = [...])]</c> attributes on the cached GET endpoints.
/// </summary>
public static class CacheTags
{
    public const string Categories = "ref:categories";
    public const string Manufacturers = "ref:manufacturers";
    public const string Suppliers = "ref:suppliers";
    public const string Companies = "ref:companies";
}
