using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DocRag.Ingestion.Api.Tests.Support;
using DocRag.Ingestion.Application.Documents;
using DocRag.Ingestion.Domain.Documents;
using NSubstitute;
using Shouldly;

namespace DocRag.Ingestion.Api.Tests;

public class DocumentsEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public DocumentsEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient().WithToken(TestTokens.Create());
        _factory.Idempotency.ClearReceivedCalls();
    }

    private static readonly object ValidBody = new
    {
        title = "Manual CT-e",
        type = "fiscal-manual",
        language = "pt-BR",
        tags = new[] { "cte" },
        metadata = new { publisher = "ENCAT" },
    };

    private static Document ExistingDocument()
    {
        var document = DocumentBuilder.New()
            .WithPublicId(Guid.CreateVersion7())
            .WithTitle("Manual")
            .OfType("fiscal-manual")
            .InLanguage("pt-BR")
            .Build(DateTimeOffset.UtcNow).Value;
        document.AssignId(1);
        return document;
    }

    private static StringContent MergePatch(string json) => new(json, new MediaTypeHeaderValue("application/merge-patch+json"));

    [Fact]
    public async Task Should_ReturnCreatedWithLocationEtagAndCorrelationHeader_When_RequestIsValid()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/documents", ValidBody);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.ETag!.Tag.ShouldBe("\"1\"");
        response.Headers.Location!.ToString().ShouldStartWith("/api/v1/documents/");
        response.Headers.Contains("X-Correlation-Id").ShouldBeTrue();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("title").GetString().ShouldBe("Manual CT-e");
        body.GetProperty("metadata").GetProperty("publisher").GetString().ShouldBe("ENCAT");
        await _factory.Documents.Received(1).InsertAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnPageWithNextCursorAndForwardFilters_When_ListingDocuments()
    {
        var rows = new[] { ExistingDocument(), ExistingDocument(), ExistingDocument() };
        _factory.Documents.ListAsync(Arg.Any<DocumentFilter>(), 3, null, Arg.Any<CancellationToken>()).Returns(rows);

        var response = await _client.GetAsync("/api/v1/documents?pageSize=2&type=fiscal-manual&language=pt-BR&tag=cte&title=manual*");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetArrayLength().ShouldBe(2);
        body.GetProperty("nextCursor").GetString().ShouldNotBeNullOrEmpty();
        await _factory.Documents.Received(1).ListAsync(
            Arg.Is<DocumentFilter>(f => f.Type == "fiscal-manual" && f.Language == "pt-BR" && f.Tag == "cte" && f.Title == "manual*"),
            3,
            null,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("cursor=%25%25%25")]
    public async Task Should_ReturnBadRequest_When_ListingWithInvalidPaging(string query)
    {
        var response = await _client.GetAsync($"/api/v1/documents?{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Should_ReturnUnauthorized_When_ListingWithoutAToken()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/documents");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Should_EchoTheCorrelationId_When_TheClientSendsOne()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/documents/{Guid.NewGuid()}");
        request.Headers.Add("X-Correlation-Id", "my-correlation-1");

        var response = await _client.SendAsync(request);

        response.Headers.GetValues("X-Correlation-Id").ShouldBe(["my-correlation-1"]);
    }

    [Fact]
    public async Task Should_RejectAnUnknownProperty_When_TheBodyCarriesOne()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/documents", new { title = "t", type = "t", language = "pt-BR", unknown = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Should_ReturnProblemJsonWithTraceIdAndFieldErrors_When_RequiredFieldsAreInvalid()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/documents", new { title = "", type = "x", language = "zz!" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("status").GetInt32().ShouldBe(400);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrEmpty();
        var errors = problem.GetProperty("errors");
        errors.TryGetProperty("title", out _).ShouldBeTrue();
        errors.TryGetProperty("language", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Should_ReturnProblemJson_When_BodyIsNotValidJson()
    {
        var response = await _client.PostAsync("/api/v1/documents", new StringContent("{not json", Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Should_ReturnProblemJson_When_ContentTypeIsNotJson()
    {
        var response = await _client.PostAsync("/api/v1/documents", new StringContent("title=x", Encoding.UTF8, "application/x-www-form-urlencoded"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_IdempotencyKeyIsLongerThan128Characters()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documents") { Content = JsonContent.Create(ValidBody) };
        request.Headers.Add("Idempotency-Key", new string('k', 129));

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Idempotency-Key");
    }

    [Fact]
    public async Task Should_ReturnNotFoundWithoutBody_When_DocumentDoesNotExist()
    {
        _factory.Documents.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Document?)null);

        var response = await _client.GetAsync($"/api/v1/documents/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_ReturnEtagOfTheCurrentRevision_When_DocumentIsRead()
    {
        var document = ExistingDocument();
        _factory.Documents.GetAsync(document.PublicId, Arg.Any<CancellationToken>()).Returns(document);

        var response = await _client.GetAsync($"/api/v1/documents/{document.PublicId}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.ShouldBe("\"1\"");
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_PatchHasNoIfMatch()
    {
        var response = await _client.PatchAsync($"/api/v1/documents/{Guid.NewGuid()}", MergePatch("""{"description":"x"}"""));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_PatchEtagIsStale()
    {
        var document = ExistingDocument();
        _factory.Documents.GetAsync(document.PublicId, Arg.Any<CancellationToken>()).Returns(document);
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/documents/{document.PublicId}") { Content = MergePatch("""{"description":"x"}""") };
        request.Headers.TryAddWithoutValidation("If-Match", "\"7\"");

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Should_ApplyMergePatchAndReturnNewEtag_When_IfMatchIsCurrent()
    {
        var document = ExistingDocument();
        _factory.Documents.GetAsync(document.PublicId, Arg.Any<CancellationToken>()).Returns(document);
        _factory.Documents.UpdateAsync(document, 1, Arg.Any<CancellationToken>()).Returns(true);
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/documents/{document.PublicId}")
        {
            Content = MergePatch("""{"description":"new","tags":["a"]}"""),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.ShouldBe("\"2\"");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("description").GetString().ShouldBe("new");
        body.GetProperty("title").GetString().ShouldBe("Manual");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"title":null}""")]
    [InlineData("""{"metadata":[1]}""")]
    public async Task Should_ReturnBadRequest_When_PatchIsEmptyOrNullsAMandatoryField(string patch)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/documents/{Guid.NewGuid()}") { Content = MergePatch(patch) };
        request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Should_ReturnBareInternalServerError_When_AnUnexpectedExceptionOccurs()
    {
        _factory.Documents.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<Document?>(_ => throw new InvalidOperationException("connection string leak"));

        var response = await _client.GetAsync($"/api/v1/documents/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }
}
