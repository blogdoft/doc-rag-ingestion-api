using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using DocRag.Ingestion.Infrastructure.Tests.Support;
using Shouldly;

namespace DocRag.Ingestion.Infrastructure.Tests.E2E;

[Collection(InfrastructureCollection.Name)]
public class IngestionFlowTests(InfrastructureFixture infrastructure) : E2ETestBase(infrastructure)
{
    [Fact]
    public async Task Should_StoreFileRegisterOutboxAndExposeUploadedStatus_When_TheDocumentedFlowIsFollowed()
    {
        var client = Client();
        var documentId = await CreateDocumentAsync(client);
        var versionId = await CreateVersionAsync(client, documentId);

        var response = await UploadAsync(client, documentId, versionId, PdfBytes, sha256: Convert.ToHexStringLower(SHA256.HashData(PdfBytes)));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var file = await ReadAsync(response);
        file.GetProperty("sha256").GetString().ShouldBe(Convert.ToHexStringLower(SHA256.HashData(PdfBytes)));
        file.GetProperty("sizeBytes").GetInt64().ShouldBe(PdfBytes.Length);
        file.EnumerateObject().Select(p => p.Name.ToLowerInvariant()).ShouldNotContain(name => name.Contains("bucket") || name.Contains("key"));

        var version = await ReadAsync(await client.GetAsync($"/api/v1/documents/{documentId}/versions/{versionId}"));
        version.GetProperty("status").GetString().ShouldBe("uploaded");
        version.GetProperty("file").GetProperty("fileName").GetString().ShouldBe("manual.pdf");

        (await ScalarAsync<long>("SELECT count(*) FROM outbox_messages WHERE event_type = 'DocumentUploaded' AND published_at IS NULL")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM document_files")).ShouldBe(1);
        var objectKey = await ScalarAsync<string>("SELECT object_key FROM document_files");
        objectKey.ShouldBe($"{documentId}/{versionId}/{file.GetProperty("id").GetGuid()}");
    }

    [Fact]
    public async Task Should_KeepArbitraryMetadata_When_DocumentIsCreatedAndRead()
    {
        var client = Client();
        var response = await client.PostAsJsonAsync("/api/v1/documents", new
        {
            title = "T",
            type = "t",
            language = "en",
            metadata = new { nested = new { list = new object[] { 1, "two", null! }, flag = true }, unicode = "ação" },
        });
        var id = (await ReadAsync(response)).GetProperty("id").GetGuid();

        var read = await ReadAsync(await client.GetAsync($"/api/v1/documents/{id}"));

        read.GetProperty("metadata").GetProperty("nested").GetProperty("list").GetArrayLength().ShouldBe(3);
        read.GetProperty("metadata").GetProperty("unicode").GetString().ShouldBe("ação");
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_LabelRepeatsInTheSameDocument()
    {
        var client = Client();
        var documentId = await CreateDocumentAsync(client);
        await CreateVersionAsync(client, documentId, "4.00");

        var duplicate = await client.PostAsJsonAsync($"/api/v1/documents/{documentId}/versions", new { label = "4.00" });

        duplicate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        duplicate.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Should_AllowTheSameLabel_When_DocumentsDiffer()
    {
        var client = Client();
        var first = await CreateDocumentAsync(client, "A");
        var second = await CreateDocumentAsync(client, "B");

        var one = await CreateVersionAsync(client, first, "4.00");
        var two = await CreateVersionAsync(client, second, "4.00");

        one.ShouldNotBe(two);
    }

    [Fact]
    public async Task Should_RejectPatchWithStaleEtagAndAllowItWithCurrentOne_When_TheDocumentChanged()
    {
        var client = Client();
        var documentId = await CreateDocumentAsync(client);

        var missing = await client.PatchAsync($"/api/v1/documents/{documentId}", MergePatch("""{"description":"x"}"""));
        var first = await Patch(client, documentId, "\"1\"", """{"description":"first"}""");
        var stale = await Patch(client, documentId, "\"1\"", """{"description":"second"}""");
        var current = await Patch(client, documentId, first.Headers.ETag!.Tag, """{"description":"third","metadata":{"publisher":null,"extra":1}}""");

        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        first.Headers.ETag!.Tag.ShouldBe("\"2\"");
        stale.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        current.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadAsync(current);
        body.GetProperty("description").GetString().ShouldBe("third");
        body.GetProperty("metadata").GetRawText().ShouldBe("""{"extra":1}""");
    }

    [Fact]
    public async Task Should_LetExactlyOneWriterWin_When_TwoPatchesUseTheSameEtagConcurrently()
    {
        var client = Client();
        var documentId = await CreateDocumentAsync(client);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Patch(client, documentId, "\"1\"", $$"""{"description":"writer-{{i}}"}""")));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest).ShouldBe(7);
        (await ScalarAsync<long>("SELECT revision FROM documents")).ShouldBe(2);
    }

    [Fact]
    public async Task Should_ReplayTheSameDocument_When_CreateIsRetriedWithTheSameKey()
    {
        var client = Client();

        var first = await CreateDocumentAsync(client, "Idem", "create-1");
        var second = await CreateDocumentAsync(client, "Idem", "create-1");

        second.ShouldBe(first);
        (await ScalarAsync<long>("SELECT count(*) FROM documents")).ShouldBe(1);
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_TheKeyIsReusedWithDifferentContent()
    {
        var client = Client();
        await CreateDocumentAsync(client, "Idem", "create-2");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documents")
        {
            Content = JsonContent.Create(new { title = "Different", type = "t", language = "pt-BR" }),
        };
        request.Headers.Add("Idempotency-Key", "create-2");

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Should_CreateOnlyOneDocument_When_TheSameKeyIsSentConcurrently()
    {
        var client = Client();

        var ids = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => CreateDocumentAsync(client, "Race", "race-1")));

        ids.Distinct().Count().ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM documents")).ShouldBe(1);
    }

    [Fact]
    public async Task Should_PageVersionsWithOpaqueCursors_When_ManyVersionsExist()
    {
        var client = Client();
        var documentId = await CreateDocumentAsync(client);
        for (var i = 1; i <= 5; i++)
        {
            await CreateVersionAsync(client, documentId, $"v{i}");
        }

        var first = await ReadAsync(await client.GetAsync($"/api/v1/documents/{documentId}/versions?pageSize=2"));
        var second = await ReadAsync(await client.GetAsync($"/api/v1/documents/{documentId}/versions?pageSize=2&cursor={Uri.EscapeDataString(first.GetProperty("nextCursor").GetString()!)}"));
        var third = await ReadAsync(await client.GetAsync($"/api/v1/documents/{documentId}/versions?pageSize=2&cursor={Uri.EscapeDataString(second.GetProperty("nextCursor").GetString()!)}"));

        var labels = new[] { first, second, third }.SelectMany(p => p.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("label").GetString())).ToList();
        labels.ShouldBe(["v5", "v4", "v3", "v2", "v1"]);
        third.GetProperty("nextCursor").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    private static StringContent MergePatch(string json) => new(json, Encoding.UTF8, new MediaTypeHeaderValue("application/merge-patch+json"));

    private static Task<HttpResponseMessage> Patch(HttpClient client, Guid documentId, string etag, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/documents/{documentId}") { Content = MergePatch(json) };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return client.SendAsync(request);
    }
}
