using System.ComponentModel.DataAnnotations;

namespace DocRag.Ingestion.Api.Authentication;

/// <summary>JWT validation settings, bound from the <c>Auth</c> configuration section.</summary>
public sealed class AuthOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Auth";

    /// <summary>Gets or sets the identity provider authority (issuer URL) used to discover signing keys.</summary>
    public string? Authority { get; set; }

    /// <summary>Gets or sets the expected token issuer. Defaults to <see cref="Authority"/>.</summary>
    public string? Issuer { get; set; }

    /// <summary>Gets or sets the expected token audience.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a symmetric HS256 key (at least 32 characters). Intended for local development
    /// and tests only; production deployments use <see cref="Authority"/>.
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>Gets or sets a value indicating whether the discovery document must be served over HTTPS.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Gets or sets the scope granting read access.</summary>
    [Required]
    public string ReadScope { get; set; } = "ingestion:read";

    /// <summary>Gets or sets the scope granting write access (which also implies read).</summary>
    [Required]
    public string WriteScope { get; set; } = "ingestion:write";

    /// <summary>
    /// Gets or sets the public (PKCE) client id Swagger UI logs in with. When set together with
    /// <see cref="Authority"/>, Swagger UI's "Authorize" redirects to the identity provider's login page.
    /// </summary>
    public string? SwaggerClientId { get; set; }

    /// <summary>
    /// Gets or sets the scopes Swagger UI requests at login. Defaults to <c>openid</c> plus
    /// <see cref="ReadScope"/> and <see cref="WriteScope"/>, which the API requires on every call.
    /// </summary>
    public string[]? SwaggerScopes { get; set; }

    /// <summary>Whether Swagger UI can offer a login redirect to the identity provider.</summary>
    /// <returns><see langword="true"/> when both <see cref="Authority"/> and <see cref="SwaggerClientId"/> are set.</returns>
    public bool HasSwaggerLogin() =>
        !string.IsNullOrWhiteSpace(Authority) && !string.IsNullOrWhiteSpace(SwaggerClientId);

    /// <summary>Resolves the scopes Swagger UI requests at login.</summary>
    /// <returns>The configured <see cref="SwaggerScopes"/>, or the defaults when none are configured.</returns>
    public string[] GetSwaggerScopes() =>
        SwaggerScopes is { Length: > 0 } ? SwaggerScopes : ["openid", ReadScope, WriteScope];

    /// <summary>Checks that exactly one key source is configured.</summary>
    /// <returns><see langword="true"/> when the combination is valid.</returns>
    public bool HasValidKeySource() =>
        !string.IsNullOrWhiteSpace(Authority) ^ (SigningKey is { Length: >= 32 });
}
