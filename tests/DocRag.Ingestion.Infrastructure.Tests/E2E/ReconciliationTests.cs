using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Files;
using DocRag.Ingestion.Infrastructure.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace DocRag.Ingestion.Infrastructure.Tests.E2E;

[Collection(InfrastructureCollection.Name)]
public class ReconciliationTests(InfrastructureFixture infrastructure) : E2ETestBase(infrastructure)
{
    private ServiceProvider BuildServices() => ServiceHost.Build(ConnectionString, Infrastructure.GarageServiceUrl, Bucket);

    [Fact]
    public async Task Should_RemoveOrphanObjectAndRecoverTheVersion_When_AnUploadDiedLeavingAnExpiredLease()
    {
        var client = Client();
        var document = await CreateDocumentAsync(client);
        var version = await CreateVersionAsync(client, document);
        await using var provider = BuildServices();
        var storage = provider.GetRequiredService<IObjectStorage>();

        // A crashed process: attempt row still active, version stuck in "uploading", bytes already in Garage.
        var orphanKey = $"{document}/{version}/crashed-upload";
        await storage.PutAsync(orphanKey, new MemoryStream(PdfBytes), "application/pdf");
        await ExecuteAsync("UPDATE document_versions SET ingestion_status = 'uploading'");
        await ExecuteAsync(
            """
            INSERT INTO upload_attempts (public_id, document_version_id, file_id, status, temporary_object_key, lease_expires_at)
            SELECT gen_random_uuid(), id, gen_random_uuid(), 'active', @key, now() - interval '5 minutes'
            FROM document_versions
            """,
            ("key", orphanKey));
        (await storage.GetSizeAsync(orphanKey)).ShouldBe(PdfBytes.Length);

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<UploadReconciler>().RunAsync(CancellationToken.None);
        }

        (await storage.GetSizeAsync(orphanKey)).ShouldBeNull();
        (await ScalarAsync<string>("SELECT status FROM upload_attempts")).ShouldBe("expired");
        (await ScalarAsync<bool>("SELECT object_cleaned FROM upload_attempts")).ShouldBeTrue();
        (await ScalarAsync<string>("SELECT ingestion_status FROM document_versions")).ShouldBe("upload_failed");
        (await UploadAsync(client, document, version, PdfBytes)).StatusCode.ShouldBe(System.Net.HttpStatusCode.Created);
    }

    [Fact]
    public async Task Should_LeaveALiveUploadAlone_When_ItsLeaseIsStillValid()
    {
        var client = Client();
        var document = await CreateDocumentAsync(client);
        var version = await CreateVersionAsync(client, document);
        var gate = new TaskCompletionSource();
        var running = UploadAsync(client, document, version, new GatedContent(PdfBytes[..10], PdfBytes[10..], gate.Task));
        await EventuallyAsync(async () => await ScalarAsync<long>("SELECT count(*) FROM upload_attempts WHERE status = 'active'") == 1, "upload active");
        await using var provider = BuildServices();

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<UploadReconciler>().RunAsync(CancellationToken.None);
        }

        (await ScalarAsync<string>("SELECT status FROM upload_attempts")).ShouldBe("active");
        (await ScalarAsync<string>("SELECT ingestion_status FROM document_versions")).ShouldBe("uploading");
        gate.SetResult();
        (await running).StatusCode.ShouldBe(System.Net.HttpStatusCode.Created);
    }

    [Fact]
    public async Task Should_FailTheLateUploadAndKeepNothing_When_ItsLeaseExpiresBeforeItFinishes()
    {
        var client = Client();
        var document = await CreateDocumentAsync(client);
        var version = await CreateVersionAsync(client, document);
        var gate = new TaskCompletionSource();
        var running = UploadAsync(client, document, version, new GatedContent(PdfBytes[..10], PdfBytes[10..], gate.Task));
        await EventuallyAsync(async () => await ScalarAsync<long>("SELECT count(*) FROM upload_attempts WHERE status = 'active'") == 1, "upload active");
        await ExecuteAsync("UPDATE upload_attempts SET lease_expires_at = now() - interval '1 minute'");
        await using var provider = BuildServices();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<UploadReconciler>().RunAsync(CancellationToken.None);
        }

        gate.SetResult();
        var response = await running;

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
        (await ScalarAsync<long>("SELECT count(*) FROM document_files")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM outbox_messages")).ShouldBe(0);
    }

    [Fact]
    public async Task Should_AbortAbandonedMultipartUploadsAndPurgeExpiredIdempotencyRecords_When_ReconciliationRuns()
    {
        using var s3 = new AmazonS3Client(
            new BasicAWSCredentials(InfrastructureFixture.GarageAccessKey, InfrastructureFixture.GarageSecretKey),
            new AmazonS3Config { ServiceURL = Infrastructure.GarageServiceUrl, AuthenticationRegion = "garage", ForcePathStyle = true });
        await s3.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest { BucketName = Bucket, Key = "abandoned" });
        await ExecuteAsync(
            "INSERT INTO idempotency_records (operation, idempotency_key, state, expires_at) VALUES ('op', 'old', 'completed', now() - interval '1 day')");
        await using var provider = ServiceHost.Build(
            ConnectionString, Infrastructure.GarageServiceUrl, Bucket, settings: new Dictionary<string, string?> { ["Reconciliation:MultipartMaxAgeMinutes"] = "1" });

        // The multipart upload is only a few ms old, so it must survive until it passes the age threshold.
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<UploadReconciler>().RunAsync(CancellationToken.None);
        }

        ((await s3.ListMultipartUploadsAsync(new ListMultipartUploadsRequest { BucketName = Bucket })).MultipartUploads ?? []).Count.ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM idempotency_records")).ShouldBe(0);

        var storage = provider.GetRequiredService<IObjectStorage>();
        (await storage.AbortStaleMultipartUploadsAsync(DateTimeOffset.UtcNow.AddMinutes(5))).ShouldBe(1);
        ((await s3.ListMultipartUploadsAsync(new ListMultipartUploadsRequest { BucketName = Bucket })).MultipartUploads ?? []).ShouldBeEmpty();
    }
}
