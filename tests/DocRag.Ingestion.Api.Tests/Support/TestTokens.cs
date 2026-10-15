using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace DocRag.Ingestion.Api.Tests.Support;

public static class TestTokens
{
    public static string Create(
        string scope = "ingestion:read ingestion:write",
        string audience = ApiFactory.Audience,
        DateTime? expires = null)
    {
        var claims = new List<Claim> { new("sub", "tester"), new("preferred_username", "tester"), new("scope", scope) };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.SigningKey)), SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            ApiFactory.Issuer,
            audience,
            claims,
            notBefore: now.AddMinutes(-10),
            expires: expires ?? now.AddMinutes(30),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler { SetDefaultTimesOnTokenCreation = false }.WriteToken(token);
    }

    public static HttpClient WithToken(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }
}
