using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using DocRag.Ingestion.Infrastructure.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace DocRag.Ingestion.Infrastructure.Tests.E2E;

[Collection(InfrastructureCollection.Name)]
public class UploadBehaviourTests(InfrastructureFixture infrastructure) : E2ETestBase(infrastructure)
{
    private async Task<(HttpClient Client, Guid Document, Guid Version)> NewVersionAsync()
    {
        var client = Client();
        var document = await CreateDocumentAsync(client);
        return (client, document, await CreateVersionAsync(client, document));
    }

    [Fact]
    public async Task Should_AcceptABodyWithoutContentLength_When_TheClientStreamsInChunks()
    {
        var (client, document, version) = await NewVersionAsync();
        var content = new GatedContent(PdfBytes[..10], PdfBytes[10..], Task.CompletedTask);
        content.Headers.ContentLength.ShouldBeNull();

        var response = await UploadAsync(client, document, version, content);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join('\n', Factory.Logs.Entries));
        (await ReadAsync(response)).GetProperty("sizeBytes").GetInt64().ShouldBe(PdfBytes.Length);
    }

    [Theory]
    [InlineData("application/zip", HttpStatusCode.BadRequest)]
    [InlineData("image/png", HttpStatusCode.BadRequest)]
    public async Task Should_RejectTheUpload_When_MimeTypeIsNotEnabled(string contentType, HttpStatusCode expected)
    {
        var (client, document, version) = await NewVersionAsync();

        var response = await UploadAsync(client, document, version, PdfBytes, contentType);

        response.StatusCode.ShouldBe(expected);
        (await ScalarAsync<long>("SELECT count(*) FROM document_files")).ShouldBe(0);
    }

    [Fact]
    public async Task Should_RejectAndLeaveTheVersionRetryable_When_SignatureDoesNotMatchTheType()
    {
        var (client, document, version) = await NewVersionAsync();

        var fake = await UploadAsync(client, document, version, "<html>definitely not a pdf</html>"u8.ToArray());
        var retry = await UploadAsync(client, document, version, PdfBytes);

        fake.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        retry.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ScalarAsync<long>("SELECT count(*) FROM outbox_messages")).ShouldBe(1);
    }

    [Fact]
    public async Task Should_RejectAnEmptyUpload_When_TheBodyHasNoBytes()
    {
        var (client, document, version) = await NewVersionAsync();

        var response = await UploadAsync(client, document, version, []);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ScalarAsync<string>("SELECT ingestion_status FROM document_versions")).ShouldBe("upload_failed");
    }

    [Fact]
    public async Task Should_RejectAndRemoveTheObject_When_Sha256DiffersFromTheAnnouncedOne()
    {
        var (client, document, version) = await NewVersionAsync();

        var response = await UploadAsync(client, document, version, PdfBytes, sha256: new string('0', 64));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ScalarAsync<long>("SELECT count(*) FROM document_files")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM outbox_messages")).ShouldBe(0);
        var storage = Factory.Services.GetRequiredService<DocRag.Ingestion.Application.Abstractions.IObjectStorage>();
        var attemptKey = await ScalarAsync<string>("SELECT temporary_object_key FROM upload_attempts");
        (await storage.GetSizeAsync(attemptKey!)).ShouldBeNull();
    }

    [Fact]
    public async Task Should_NeverReplaceTheOriginal_When_AnotherFileIsUploadedAfterCompletion()
    {
        var (client, document, version) = await NewVersionAsync();
        await UploadAsync(client, document, version, PdfBytes);

        var second = await UploadAsync(client, document, version, "%PDF-1.4\nother content\n"u8.ToArray());

        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ScalarAsync<long>("SELECT count(*) FROM document_files")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM outbox_messages")).ShouldBe(1);
    }

    [Fact]
    public async Task Should_ProduceASingleCompletedFile_When_TwoUploadsOfTheSameVersionRaceEachOther()
    {
        var (client, document, version) = await NewVersionAsync();
        var gate = new TaskCompletionSource();
        var first = UploadAsync(client, document, version, new GatedContent(PdfBytes[..10], PdfBytes[10..], gate.Task));
        await EventuallyAsync(
            async () => await ScalarAsync<long>("SELECT count(*) FROM upload_attempts WHERE status = 'active'") == 1,
            "the first upload holds the active attempt");

        var second = await UploadAsync(client, document, version, PdfBytes);
        gate.SetResult();
        var firstResponse = await first;

        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ScalarAsync<long>("SELECT count(*) FROM document_files")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM outbox_messages")).ShouldBe(1);
    }

    [Fact]
    public async Task Should_NeverCreateTwoFiles_When_ManyUploadsHitTheSameVersionAtOnce()
    {
        var (client, document, version) = await NewVersionAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => UploadAsync(client, document, version, PdfBytes)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created).ShouldAllBe(r => r.StatusCode == HttpStatusCode.BadRequest);
        (await ScalarAsync<long>("SELECT count(*) FROM document_files")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM outbox_messages")).ShouldBe(1);
    }

    [Fact]
    public async Task Should_RecordFailureAndAllowRetry_When_TheClientCancelsMidTransfer()
    {
        var (client, document, version) = await NewVersionAsync();
        using var cancellation = new CancellationTokenSource();
        var neverOpened = new TaskCompletionSource();
        var interrupted = UploadAsync(
            client, document, version, new GatedContent(PdfBytes[..10], PdfBytes[10..], neverOpened.Task), cancellationToken: cancellation.Token);
        await EventuallyAsync(
            async () => await ScalarAsync<long>("SELECT count(*) FROM upload_attempts WHERE status = 'active'") == 1,
            "the interrupted upload is in flight");

        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => interrupted);

        await EventuallyAsync(
            async () => await ScalarAsync<string>("SELECT ingestion_status FROM document_versions") == "upload_failed",
            "the cancelled attempt is recorded as failed");
        (await ScalarAsync<long>("SELECT count(*) FROM outbox_messages")).ShouldBe(0);
        (await UploadAsync(client, document, version, PdfBytes)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Should_ReturnTheOriginalResultWithoutANewEvent_When_AnUploadIsRetriedWithTheSameKeyAndBytes()
    {
        var (client, document, version) = await NewVersionAsync();
        var first = await UploadAsync(client, document, version, PdfBytes, idempotencyKey: "upload-1");

        var replay = await UploadAsync(client, document, version, PdfBytes, idempotencyKey: "upload-1");

        replay.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReadAsync(replay)).GetProperty("id").GetGuid().ShouldBe((await ReadAsync(first)).GetProperty("id").GetGuid());
        (await ScalarAsync<long>("SELECT count(*) FROM outbox_messages")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM document_files")).ShouldBe(1);
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_TheSameKeyIsUsedWithDifferentBytes()
    {
        var (client, document, version) = await NewVersionAsync();
        await UploadAsync(client, document, version, PdfBytes, idempotencyKey: "upload-2");

        var different = await UploadAsync(client, document, version, "%PDF-1.4\ndifferent\n"u8.ToArray(), idempotencyKey: "upload-2");

        different.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ScalarAsync<long>("SELECT count(*) FROM outbox_messages")).ShouldBe(1);
    }

    [Fact]
    public async Task Should_KeepMemoryAlmostConstant_When_AFileMuchLargerThanTheHeapIsStreamed()
    {
        const long size = 300L * 1024 * 1024;
        var (client, document, version) = await NewVersionAsync();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var baseline = Process.GetCurrentProcess().WorkingSet64;
        long peak = baseline;
        using var stop = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                peak = Math.Max(peak, Process.GetCurrentProcess().WorkingSet64);
                await Task.Delay(25);
            }
        });
        var content = new GeneratedPdfContent(size);

        var response = await UploadAsync(client, document, version, content);
        await stop.CancelAsync();
        await sampler;

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var file = await ReadAsync(response);
        file.GetProperty("sizeBytes").GetInt64().ShouldBe(size);
        file.GetProperty("sha256").GetString().ShouldBe(content.Sha256);
        var growthMiB = (peak - baseline) / 1024 / 1024;
        growthMiB.ShouldBeLessThan(180, $"a {size / 1024 / 1024} MiB upload must not be buffered (working set grew {growthMiB} MiB)");
    }
}

[Collection(InfrastructureCollection.Name)]
public class UploadLimitTests(InfrastructureFixture infrastructure) : E2ETestBase(infrastructure)
{
    protected override IDictionary<string, string?>? ConfigurationOverrides => new Dictionary<string, string?>
    {
        ["Upload:MaxBytes"] = "2048",
        ["Upload:MaxConcurrentUploads"] = "1",
    };

    [Fact]
    public async Task Should_ReturnBadRequestAndStoreNothing_When_ChunkedBodyExceedsTheLimit()
    {
        var client = Client();
        var document = await CreateDocumentAsync(client);
        var version = await CreateVersionAsync(client, document);
        var big = new GatedContent(PdfBytes, new byte[4096], Task.CompletedTask);

        var response = await UploadAsync(client, document, version, big);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await ScalarAsync<long>("SELECT count(*) FROM document_files")).ShouldBe(0);
        (await ScalarAsync<string>("SELECT ingestion_status FROM document_versions")).ShouldBe("upload_failed");
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_TheServiceExceedsItsConcurrentUploads()
    {
        var client = Client();
        var firstDocument = await CreateDocumentAsync(client, "A");
        var firstVersion = await CreateVersionAsync(client, firstDocument);
        var secondDocument = await CreateDocumentAsync(client, "B");
        var secondVersion = await CreateVersionAsync(client, secondDocument);
        var gate = new TaskCompletionSource();
        var running = UploadAsync(client, firstDocument, firstVersion, new GatedContent(PdfBytes[..10], PdfBytes[10..], gate.Task));
        await EventuallyAsync(
            async () => await ScalarAsync<long>("SELECT count(*) FROM upload_attempts WHERE status = 'active'") == 1,
            "first upload active");

        var rejected = await UploadAsync(client, secondDocument, secondVersion, PdfBytes);
        gate.SetResult();
        await running;

        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
