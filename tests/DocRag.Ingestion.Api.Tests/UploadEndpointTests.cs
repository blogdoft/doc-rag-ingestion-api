using System.Net;
using System.Net.Http.Headers;
using DocRag.Ingestion.Api.Tests.Support;
using Shouldly;

namespace DocRag.Ingestion.Api.Tests;

public class UploadEndpointTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public UploadEndpointTests(ApiFactory factory) => _client = factory.CreateClient().WithToken(TestTokens.Create());

    private static string Url => $"/api/v1/documents/{Guid.NewGuid()}/versions/{Guid.NewGuid()}/files";

    private static HttpRequestMessage Upload(byte[] body, string contentType = "application/pdf", string? disposition = "attachment; filename=\"a.pdf\"")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        if (disposition is not null)
        {
            request.Content.Headers.TryAddWithoutValidation("Content-Disposition", disposition);
        }

        return request;
    }

    [Fact]
    public async Task Should_ReturnPayloadTooLargeProblem_When_DeclaredLengthExceedsTheLimit()
    {
        var response = await _client.SendAsync(Upload(new byte[2048]));

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Should_ReturnUnsupportedMediaTypeProblem_When_ContentTypeIsNotEnabled()
    {
        var response = await _client.SendAsync(Upload([1, 2, 3], "application/zip"));

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Should_ReturnBadRequestProblem_When_ContentDispositionIsMissing()
    {
        var response = await _client.SendAsync(Upload("%PDF-1"u8.ToArray(), disposition: null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_ChecksumHeaderIsMalformed()
    {
        var request = Upload("%PDF-1"u8.ToArray());
        request.Headers.Add("X-Content-SHA256", "not-a-digest");

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("X-Content-SHA256");
    }
}
