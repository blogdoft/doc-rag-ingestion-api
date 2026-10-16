using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DocRag.Ingestion.Api.Authentication;

/// <summary>Registers JWT bearer authentication and the scope authorization policies.</summary>
public static class AuthenticationExtensions
{
    /// <summary>Policy requiring the read (or write) scope.</summary>
    public const string ReadPolicy = "ingestion-read";

    /// <summary>Policy requiring the write scope.</summary>
    public const string WritePolicy = "ingestion-write";

    /// <summary>
    /// Validates signature, issuer, audience and expiry; every endpoint is protected by default.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddIngestionAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.HasValidKeySource(), "Configure exactly one of 'Auth:Authority' or 'Auth:SigningKey' (32+ characters).")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>>((bearer, auth) =>
            {
                var options = auth.Value;
                var signingKey = string.IsNullOrWhiteSpace(options.SigningKey) ? null : options.SigningKey;
                bearer.MapInboundClaims = false;
                bearer.RequireHttpsMetadata = options.RequireHttpsMetadata;
                bearer.Authority = options.Authority;
                if (!string.IsNullOrWhiteSpace(options.MetadataAddress))
                {
                    bearer.MetadataAddress = options.MetadataAddress;
                }

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer ?? options.Authority ?? "doc-rag-ingestion-dev",
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    IssuerSigningKey = signingKey is null ? null : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ValidAlgorithms = signingKey is null ? null : [SecurityAlgorithms.HmacSha256],
                };
                bearer.Events = new JwtBearerEvents { OnChallenge = RejectUnauthenticated, OnForbidden = RejectForbidden };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.AddOptions<AuthorizationOptions>()
            .Configure<IOptions<AuthOptions>>((authorization, auth) =>
            {
                var options = auth.Value;
                authorization.AddPolicy(ReadPolicy, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireAssertion(context => ClaimsPrincipalExtensions.HasScope(context.User, options.ReadScope, options.WriteScope)));
                authorization.AddPolicy(WritePolicy, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireAssertion(context => ClaimsPrincipalExtensions.HasScope(context.User, options.WriteScope)));
            });

        return services;
    }

    // 401 carries no body: why the token was rejected is only recorded in the application log.
    private static Task RejectUnauthenticated(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;

        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("DocRag.Ingestion.Api.Authentication");
        logger.LogInformation(
            "Request {Method} {Path} rejected with 401: {Error}",
            context.Request.Method,
            context.Request.Path,
            context.AuthenticateFailure?.GetType().Name ?? "no credentials");
        return Task.CompletedTask;
    }

    // 403 carries no body either, but the user name is logged for auditing.
    private static Task RejectForbidden(ForbiddenContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("DocRag.Ingestion.Api.Authentication");
        logger.LogWarning(
            "Request {Method} {Path} forbidden for {Username}", context.Request.Method, context.Request.Path, context.HttpContext.User.GetUsername());
        return Task.CompletedTask;
    }
}
