using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace DocRag.Ingestion.Api.Authentication;

/// <summary>Helpers reading the trusted identity facts of the caller.</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>Best human-readable identity of the caller for log lines.</summary>
    /// <param name="principal">The caller, possibly <see langword="null"/>.</param>
    /// <returns>The user name for logging.</returns>
    public static string GetUsername(this ClaimsPrincipal? principal) =>
        principal?.FindFirst("preferred_username")?.Value
        ?? principal?.FindFirst("name")?.Value
        ?? principal?.FindFirst("sub")?.Value
        ?? "anonymous";

    /// <summary>Whether the token carries at least one of the scopes (<c>scope</c> or <c>scp</c> claims).</summary>
    /// <param name="principal">The caller.</param>
    /// <param name="scopes">Acceptable scopes.</param>
    /// <returns><see langword="true"/> when one is present.</returns>
    public static bool HasScope(ClaimsPrincipal principal, params string[] scopes) =>
        principal.FindAll("scope").Concat(principal.FindAll("scp"))
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Any(granted => scopes.Contains(granted, StringComparer.Ordinal));
}
