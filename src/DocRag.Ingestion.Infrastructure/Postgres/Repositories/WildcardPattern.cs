using System.Text;

namespace DocRag.Ingestion.Infrastructure.Postgres.Repositories;

/// <summary>Turns a user-facing wildcard search into a PostgreSQL <c>LIKE</c> pattern.</summary>
public static class WildcardPattern
{
    /// <summary>The escape character the generated pattern relies on (<c>LIKE ... ESCAPE '\'</c>).</summary>
    public const char Escape = '\\';

    /// <summary>
    /// Converts <c>*</c> into <c>%</c> and neutralizes every other <c>LIKE</c> metacharacter, so only
    /// <c>*</c> acts as a wildcard. Consecutive <c>*</c> collapse into one. The pattern is anchored exactly as
    /// written: a value without any <c>*</c> is an equality match, <c>manual*</c> starts with and
    /// <c>*cte</c> ends with.
    /// </summary>
    /// <param name="search">The user-supplied search text.</param>
    /// <returns>A pattern to bind as a parameter of an <c>ILIKE</c> comparison.</returns>
    public static string ToLikePattern(string search)
    {
        var pattern = new StringBuilder(search.Length);
        var previousWasWildcard = false;
        foreach (var character in search)
        {
            if (character == '*')
            {
                if (!previousWasWildcard)
                {
                    pattern.Append('%');
                }

                previousWasWildcard = true;
                continue;
            }

            previousWasWildcard = false;
            if (character is Escape or '%' or '_')
            {
                pattern.Append(Escape);
            }

            pattern.Append(character);
        }

        return pattern.ToString();
    }
}
