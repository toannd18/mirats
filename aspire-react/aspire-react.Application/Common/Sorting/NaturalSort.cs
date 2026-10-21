namespace aspire_react.Server.Application.Common.Sorting;

/// <summary>
/// Shared PostgreSQL collation for natural (numeric-aware) ordering of human codes
/// (asset tag, item number, batch number, ...). Created by migration
/// <c>AddAssetTagNaturalCollation</c> with ICU locale <c>und-u-kn-true</c>.
/// </summary>
public static class NaturalSort
{
    public const string Collation = "asset_tag_natural";
}
