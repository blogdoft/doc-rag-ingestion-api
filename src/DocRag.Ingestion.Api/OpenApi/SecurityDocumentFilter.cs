using DocRag.Ingestion.Api.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace DocRag.Ingestion.Api.OpenApi;

/// <summary>
/// Declares Bearer (JWT) authentication so Swagger UI offers an "Authorize" button and, when
/// <see cref="AuthOptions.HasSwaggerLogin"/>, an <c>OAuth2</c> authorization-code scheme that sends the
/// user to the identity provider's login page instead of pasting a token. Either satisfies the global
/// security requirement.
/// </summary>
/// <param name="authOptions">Validated authentication options, from which the login URLs are derived.</param>
internal sealed class SecurityDocumentFilter(IOptions<AuthOptions> authOptions) : IDocumentFilter
{
    internal const string BearerSchemeName = "Bearer";
    internal const string OAuth2SchemeName = "OAuth2";

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        swaggerDoc.Components ??= new OpenApiComponents();
        swaggerDoc.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        swaggerDoc.Security ??= [];

        swaggerDoc.Components.SecuritySchemes[BearerSchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "JWT access token validated by signature, issuer, audience and expiry. Paste the token only, without the 'Bearer ' prefix. Read operations need the 'ingestion:read' scope and write operations 'ingestion:write'. Local development tokens: see scripts/dev-token.sh.",
        };
        swaggerDoc.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(BearerSchemeName, swaggerDoc)] = [],
        });

        var options = authOptions.Value;
        if (!options.HasSwaggerLogin())
        {
            return;
        }

        var openIdConnect = $"{options.Authority!.TrimEnd('/')}/protocol/openid-connect";
        var scopes = options.GetSwaggerScopes();
        swaggerDoc.Components.SecuritySchemes[OAuth2SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Description = "Log in through the identity provider (authorization code flow with PKCE).",
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = new Uri($"{openIdConnect}/auth"),
                    TokenUrl = new Uri($"{openIdConnect}/token"),
                    Scopes = scopes.ToDictionary(scope => scope, scope => scope),
                },
            },
        };
        swaggerDoc.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(OAuth2SchemeName, swaggerDoc)] = [.. scopes],
        });
    }
}
