using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlogDoFT.Libs.ResultPattern;
using DocRag.Ingestion.Api.Tests.Support;
using DocRag.Ingestion.Domain.MasterData;
using NSubstitute;
using Shouldly;

namespace DocRag.Ingestion.Api.Tests;

public class DocumentTypesEndpointTests : IClassFixture<ApiFactory>
{
    private const string Url = "/api/v1/document-types";

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public DocumentTypesEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient().WithToken(TestTokens.Create());
        _factory.DocumentTypes.ClearReceivedCalls();
    }

    private static DocumentType Existing(string name = "fiscal-manual")
    {
        var documentType = DocumentType.Builder().WithPublicId(Guid.CreateVersion7()).Named(name).Build(DateTimeOffset.UtcNow).Value;
        documentType.AssignId(1);
        return documentType;
    }

    [Fact]
    public async Task Should_ReturnCreatedWithLocationAndPublicId_When_RequestIsValid()
    {
        _factory.DocumentTypes.InsertAsync(Arg.Any<DocumentType>(), Arg.Any<CancellationToken>()).Returns(Result.AsSuccess());

        var response = await _client.PostAsJsonAsync(Url, new { name = "fiscal-manual" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        response.Headers.Location!.ToString().ShouldBe($"{Url}/{id}");
        body.GetProperty("name").GetString().ShouldBe("fiscal-manual");
        body.TryGetProperty("internalId", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Should_ReturnProblemWithFieldError_When_NameIsMissing()
    {
        var response = await _client.PostAsJsonAsync(Url, new { name = "" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").TryGetProperty("name", out _).ShouldBeTrue();
        await _factory.DocumentTypes.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_TheNameAlreadyExists()
    {
        _factory.DocumentTypes.InsertAsync(Arg.Any<DocumentType>(), Arg.Any<CancellationToken>())
            .Returns(new Failure("400-document-type-name-exists", "A document type named 'x' already exists."));

        var response = await _client.PostAsJsonAsync(Url, new { name = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Should_ReturnDocumentType_When_ItExists()
    {
        var existing = Existing();
        _factory.DocumentTypes.GetAsync(existing.PublicId, Arg.Any<CancellationToken>()).Returns(existing);

        var response = await _client.GetAsync($"{Url}/{existing.PublicId}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().ShouldBe(existing.PublicId);
    }

    [Fact]
    public async Task Should_ReturnNotFoundWithoutBody_When_ItDoesNotExist()
    {
        _factory.DocumentTypes.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((DocumentType?)null);

        var response = await _client.GetAsync($"{Url}/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_ReturnPageWithNextCursor_When_Listing()
    {
        _factory.DocumentTypes.ListAsync(3, null, Arg.Any<CancellationToken>())
            .Returns(new List<DocumentType> { Existing("a"), Existing("b"), Existing("c") });

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
    public async Task Should_ReturnUpdatedDocumentType_When_ReplacingTheName()
    {
        var existing = Existing();
        _factory.DocumentTypes.GetAsync(existing.PublicId, Arg.Any<CancellationToken>()).Returns(existing);
        _factory.DocumentTypes.UpdateAsync(existing, Arg.Any<CancellationToken>()).Returns(Result.AsSuccess());

        var response = await _client.PutAsJsonAsync($"{Url}/{existing.PublicId}", new { name = "legal-opinion" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("name").GetString().ShouldBe("legal-opinion");
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_ReplacingAnUnknownDocumentType()
    {
        _factory.DocumentTypes.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((DocumentType?)null);

        var response = await _client.PutAsJsonAsync($"{Url}/{Guid.NewGuid()}", new { name = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Should_ReturnNoContent_When_Deleting()
    {
        _factory.DocumentTypes.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var response = await _client.DeleteAsync($"{Url}/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_DeletingAnUnknownDocumentType()
    {
        _factory.DocumentTypes.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        var response = await _client.DeleteAsync($"{Url}/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Should_ReturnForbidden_When_WritingWithReadOnlyScope()
    {
        var client = _factory.CreateClient().WithToken(TestTokens.Create(scope: "ingestion:read"));

        var response = await client.PostAsJsonAsync(Url, new { name = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Should_ReturnUnauthorized_When_ListingWithoutAToken()
    {
        var response = await _factory.CreateClient().GetAsync(Url);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
