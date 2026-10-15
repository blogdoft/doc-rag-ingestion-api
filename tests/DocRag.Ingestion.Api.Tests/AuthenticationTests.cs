using System.Net;
using System.Net.Http.Json;
using DocRag.Ingestion.Api.Tests.Support;
using Shouldly;

namespace DocRag.Ingestion.Api.Tests;

public class AuthenticationTests : IClassFixture<ApiFactory>
{
    private static readonly string DocumentUrl = $"/api/v1/documents/{Guid.NewGuid()}";
    private readonly ApiFactory _factory;

    public AuthenticationTests(ApiFactory factory) => _factory = factory;

    private HttpClient Client(string? token)
    {
        var client = _factory.CreateClient();
        return token is null ? client : client.WithToken(token);
    }

    [Fact]
    public async Task Should_ReturnUnauthorizedWithoutBody_When_TokenIsMissing()
    {
        var response = await Client(null).GetAsync(DocumentUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_ReturnUnauthorized_When_TokenIsExpired()
    {
        var token = TestTokens.Create(expires: DateTime.UtcNow.AddMinutes(-5));

        var response = await Client(token).GetAsync(DocumentUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Should_ReturnUnauthorized_When_AudienceIsWrong()
    {
        var response = await Client(TestTokens.Create(audience: "someone-else")).GetAsync(DocumentUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Should_ReturnUnauthorized_When_SignatureIsInvalid()
    {
        var token = TestTokens.Create()[..^3] + "abc";

        var response = await Client(token).GetAsync(DocumentUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Should_ReturnForbidden_When_WritingWithReadOnlyScope()
    {
        var client = Client(TestTokens.Create(scope: "ingestion:read"));

        var response = await client.PostAsJsonAsync("/api/v1/documents", new { title = "t", type = "t", language = "pt-BR" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Should_ReturnForbidden_When_TokenHasNoIngestionScope()
    {
        var response = await Client(TestTokens.Create(scope: "openid")).GetAsync(DocumentUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Should_KeepHealthProbesAnonymous_When_NoTokenIsSent()
    {
        var response = await Client(null).GetAsync("/health/live");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
