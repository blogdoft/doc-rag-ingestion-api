using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlogDoFT.Libs.ResultPattern;
using DocRag.Ingestion.Api.Tests.Support;
using DocRag.Ingestion.Domain.MasterData;
using NSubstitute;
using Shouldly;

namespace DocRag.Ingestion.Api.Tests;

public class DocumentLanguagesEndpointTests : IClassFixture<ApiFactory>
{
    private const string Url = "/api/v1/document-languages";

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public DocumentLanguagesEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient().WithToken(TestTokens.Create());
        _factory.DocumentLanguages.ClearReceivedCalls();
    }

    private static DocumentLanguage Existing(string code = "pt-BR")
    {
        var documentType = DocumentLanguage.Builder().WithPublicId(Guid.CreateVersion7()).WithCode(code).Build(DateTimeOffset.UtcNow).Value;
        documentType.AssignId(1);
        return documentType;
    }

    [Fact]
    public async Task Should_ReturnCreatedWithLocationAndPublicId_When_RequestIsValid()
    {
        _factory.DocumentLanguages.InsertAsync(Arg.Any<DocumentLanguage>(), Arg.Any<CancellationToken>()).Returns(Result.AsSuccess());

        var response = await _client.PostAsJsonAsync(Url, new { code = "pt-BR" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        response.Headers.Location!.ToString().ShouldBe($"{Url}/{id}");
        body.GetProperty("code").GetString().ShouldBe("pt-BR");
        body.TryGetProperty("internalId", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Should_ReturnProblemWithFieldError_When_CodeIsMissing()
    {
        var response = await _client.PostAsJsonAsync(Url, new { code = "" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").TryGetProperty("code", out _).ShouldBeTrue();
        await _factory.DocumentLanguages.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_TheCodeAlreadyExists()
    {
        _factory.DocumentLanguages.InsertAsync(Arg.Any<DocumentLanguage>(), Arg.Any<CancellationToken>())
            .Returns(new Failure("400-document-language-code-exists", "A document language with code 'xx' already exists."));

        var response = await _client.PostAsJsonAsync(Url, new { code = "xx" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Should_ReturnDocumentLanguage_When_ItExists()
    {
        var existing = Existing();
        _factory.DocumentLanguages.GetAsync(existing.PublicId, Arg.Any<CancellationToken>()).Returns(existing);

        var response = await _client.GetAsync($"{Url}/{existing.PublicId}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().ShouldBe(existing.PublicId);
    }

    [Fact]
    public async Task Should_ReturnNotFoundWithoutBody_When_ItDoesNotExist()
    {
        _factory.DocumentLanguages.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((DocumentLanguage?)null);

        var response = await _client.GetAsync($"{Url}/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_ReturnPageWithNextCursor_When_Listing()
    {
        _factory.DocumentLanguages.ListAsync(3, null, Arg.Any<CancellationToken>())
            .Returns(new List<DocumentLanguage> { Existing("en"), Existing("es"), Existing("fr") });

        var response = await _client.GetAsync($"{Url}?pageSize=2");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetArrayLength().ShouldBe(2);
        body.GetProperty("nextCursor").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_ListingWithInvalidPageSize()
    {
        var response = await _client.GetAsync($"{Url}?pageSize=0");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Should_ReturnUpdatedDocumentLanguage_When_ReplacingTheCode()
    {
        var existing = Existing();
        _factory.DocumentLanguages.GetAsync(existing.PublicId, Arg.Any<CancellationToken>()).Returns(existing);
        _factory.DocumentLanguages.UpdateAsync(existing, Arg.Any<CancellationToken>()).Returns(Result.AsSuccess());

        var response = await _client.PutAsJsonAsync($"{Url}/{existing.PublicId}", new { code = "en-US" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().ShouldBe("en-US");
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_ReplacingAnUnknownDocumentLanguage()
    {
        _factory.DocumentLanguages.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((DocumentLanguage?)null);

        var response = await _client.PutAsJsonAsync($"{Url}/{Guid.NewGuid()}", new { code = "xx" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Should_ReturnNoContent_When_Deleting()
    {
        _factory.DocumentLanguages.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var response = await _client.DeleteAsync($"{Url}/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_DeletingAnUnknownDocumentLanguage()
    {
        _factory.DocumentLanguages.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        var response = await _client.DeleteAsync($"{Url}/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Should_ReturnForbidden_When_WritingWithReadOnlyScope()
    {
        var client = _factory.CreateClient().WithToken(TestTokens.Create(scope: "ingestion:read"));

        var response = await client.PostAsJsonAsync(Url, new { code = "xx" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Should_ReturnUnauthorized_When_ListingWithoutAToken()
    {
        var response = await _factory.CreateClient().GetAsync(Url);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
