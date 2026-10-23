using System.Globalization;
using System.Text;

namespace aspire_react.Server.Application.Search;

/// <summary>
/// Maps PostgreSQL's unaccent() SQL function into LINQ. The extension is installed by the
/// AddUnaccentExtension migration; the C# side also normalises user input (accents/diacritics)
/// so the query works for both "may in" and "Máy in".
/// </summary>
public static class SearchDbFunctions
{
    public static string Unaccent(string value) =>
        throw new NotSupportedException("SearchDbFunctions.Unaccent is translated by EF Core/PostgreSQL.");

    public static string NormalizeQuery(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }

        // Collapse whitespace so "  may   in " and "may in" are equivalent.
        return string.Join(' ', builder.ToString()
            .Normalize(NormalizationForm.FormC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
