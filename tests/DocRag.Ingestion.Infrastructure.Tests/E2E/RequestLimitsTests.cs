using System.Net;
using System.Net.Http.Headers;
using DocRag.Ingestion.Infrastructure.Tests.Support;
using Shouldly;

namespace DocRag.Ingestion.Infrastructure.Tests.E2E;

[Collection(InfrastructureCollection.Name)]
public class RequestLimitsTests(InfrastructureFixture infrastructure) : E2ETestBase(infrastructure)
{
    private static string UploadUrl => $"/api/v1/documents/{Guid.NewGuid()}/versions/{Guid.NewGuid()}/files";

    [Fact]
    public async Task Should_ReturnPayloadTooLarge_When_AJsonBodyExceedsOneMiB()
    {
        var client = Client();
        var json = $$"""{"title":"{{new string('a', 1_500_000)}}","type":"t","language":"en"}""";

        var response = await client.PostAsync("/api/v1/documents", new StringContent(json, new MediaTypeHeaderValue("application/json")));

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Should_ReturnUnauthorizedAndKeepServing_When_AnUnauthenticatedUploadSendsALargeBody()
    {
        var client = Factory.CreateDefaultClient();
        using var body = new ByteArrayContent(new byte[3 * 1024 * 1024]);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        var rejected = await client.PostAsync(UploadUrl, body);
        var next = await client.GetAsync("/health/live");

        rejected.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        next.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
