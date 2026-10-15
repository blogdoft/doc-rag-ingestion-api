using System.Text.Json;
using Confluent.Kafka;
using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Outbox;
using DocRag.Ingestion.Infrastructure.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace DocRag.Ingestion.Infrastructure.Tests.Messaging;

[Collection(InfrastructureCollection.Name)]
public class KafkaOutboxDispatchTests(InfrastructureFixture infrastructure) : E2ETestBase(infrastructure)
{
    private static readonly string[] AllowedEventProperties =
        ["eventId", "eventType", "eventVersion", "occurredAt", "correlationId", "documentId", "versionId", "fileId", "contentType", "sizeBytes", "sha256", "storage"];

    private ServiceProvider BuildWorkerServices(Action<IServiceCollection>? configure = null) =>
        ServiceHost.Build(ConnectionString, Infrastructure.GarageServiceUrl, Bucket, Infrastructure.KafkaBootstrapServers, configure);

    private async Task<(Guid Document, Guid Version, JsonElement File)> IngestAsync(string correlationId = "corr-123")
    {
        var client = Client();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
        var document = await CreateDocumentAsync(client);
        var version = await CreateVersionAsync(client, document);
        var response = await UploadAsync(client, document, version, PdfBytes);
        response.EnsureSuccessStatusCode();
        return (document, version, await ReadAsync(response));
    }

    private List<ConsumeResult<string, string>> Consume(int expected, string? onlyKey = null)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = Infrastructure.KafkaBootstrapServers,
            GroupId = $"test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Subscribe("doc-rag.documents.uploaded.v1");

        var results = new List<ConsumeResult<string, string>>();
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (results.Count < expected && DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromSeconds(1));
            if (result is not null && (onlyKey is null || result.Message.Key == onlyKey))
            {
                results.Add(result);
            }
        }

        return results;
    }

    [Fact]
    public async Task Should_PublishTheContractPayloadKeyedByDocument_When_TheOutboxIsDispatched()
    {
        var (document, version, file) = await IngestAsync();
        await using var provider = BuildWorkerServices();
        await using var scope = provider.CreateAsyncScope();

        var published = await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(CancellationToken.None);

        published.ShouldBe(1);
        var message = Consume(1, document.ToString()).Single();
        var payload = JsonDocument.Parse(message.Message.Value).RootElement;
        payload.EnumerateObject().Select(p => p.Name).Order().ShouldBe(AllowedEventProperties.Order());
        payload.GetProperty("eventType").GetString().ShouldBe("DocumentUploaded");
        payload.GetProperty("eventVersion").GetInt32().ShouldBe(1);
        payload.GetProperty("correlationId").GetString().ShouldBe("corr-123");
        payload.GetProperty("documentId").GetGuid().ShouldBe(document);
        payload.GetProperty("versionId").GetGuid().ShouldBe(version);
        payload.GetProperty("fileId").GetGuid().ShouldBe(file.GetProperty("id").GetGuid());
        payload.GetProperty("sha256").GetString().ShouldBe(file.GetProperty("sha256").GetString());
        payload.GetProperty("storage").GetProperty("provider").GetString().ShouldBe("garage-s3");
        payload.GetProperty("storage").GetProperty("bucket").GetString().ShouldBe(Bucket);
        payload.GetProperty("storage").GetProperty("objectKey").GetString()!.ShouldContain(version.ToString());
        message.Message.Value.ShouldNotContain("X-Amz-", Case.Insensitive);
        (await ScalarAsync<long>("SELECT count(*) FROM outbox_messages WHERE published_at IS NOT NULL")).ShouldBe(1);
    }

    [Fact]
    public async Task Should_PublishNothing_When_TheOutboxIsAlreadyEmpty()
    {
        await IngestAsync();
        await using var provider = BuildWorkerServices();
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
        await dispatcher.DispatchBatchAsync(CancellationToken.None);

        var second = await dispatcher.DispatchBatchAsync(CancellationToken.None);

        second.ShouldBe(0);
    }

    [Fact]
    public async Task Should_RetryWithBackoffAndDeliverLater_When_TheBrokerIsUnavailableThenRecovers()
    {
        var (document, _, _) = await IngestAsync();
        var failing = new ToggledPublisher();
        await using var provider = BuildWorkerServices(services => services.AddSingleton<IEventPublisher>(failing));
        await using (var scope = provider.CreateAsyncScope())
        {
            var failed = await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(CancellationToken.None);
            failed.ShouldBe(0);
        }

        (await ScalarAsync<int>("SELECT attempts FROM outbox_messages")).ShouldBe(1);
        (await ScalarAsync<string>("SELECT last_error FROM outbox_messages")).ShouldBe("broker unavailable");
        (await ScalarAsync<bool>("SELECT next_attempt_at > now() + interval '3 seconds' FROM outbox_messages")).ShouldBeTrue();
        await ExecuteAsync("UPDATE outbox_messages SET next_attempt_at = now() - interval '1 second'");
        failing.Healthy = true;

        await using var retryScope = provider.CreateAsyncScope();
        var recovered = await retryScope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(CancellationToken.None);

        recovered.ShouldBe(1);
        failing.Published.Single().Key.ShouldBe(document.ToString());
        (await ScalarAsync<long>("SELECT count(*) FROM outbox_messages WHERE published_at IS NOT NULL")).ShouldBe(1);
    }

    [Fact]
    public async Task Should_NotLoseTheEvent_When_TheProcessDiesAfterPublishingAndBeforeMarkingIt()
    {
        var (document, _, _) = await IngestAsync();
        await using var provider = BuildWorkerServices();

        // First "process": publishes, then crashes before MarkPublishedAsync.
        OutboxMessage claimed;
        await using (var crashed = provider.CreateAsyncScope())
        {
            claimed = (await crashed.ServiceProvider.GetRequiredService<IOutboxRepository>()
                .ClaimBatchAsync(10, TimeSpan.FromMinutes(2), DateTimeOffset.UtcNow)).Single();
            await crashed.ServiceProvider.GetRequiredService<IEventPublisher>().PublishAsync(claimed);
        }

        // Its lease expires; a second process claims and publishes the same message again.
        await ExecuteAsync("UPDATE outbox_messages SET lease_until = now() - interval '1 second'");
        await using (var recovery = provider.CreateAsyncScope())
        {
            (await recovery.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(CancellationToken.None)).ShouldBe(1);
        }

        var delivered = Consume(2, document.ToString());
        delivered.Count.ShouldBe(2);
        delivered.Select(d => JsonDocument.Parse(d.Message.Value).RootElement.GetProperty("eventId").GetGuid())
            .Distinct().Single().ShouldBe(claimed.EventId);
        (await ScalarAsync<long>("SELECT count(*) FROM outbox_messages WHERE published_at IS NOT NULL")).ShouldBe(1);
    }

    [Fact]
    public async Task Should_KeepOneDocumentsEventsInOrderOnTheSamePartition_When_SeveralVersionsAreIngested()
    {
        var client = Client();
        var document = await CreateDocumentAsync(client);
        for (var i = 1; i <= 3; i++)
        {
            var version = await CreateVersionAsync(client, document, $"v{i}");
            (await UploadAsync(client, document, version, PdfBytes)).EnsureSuccessStatusCode();
        }

        await using var provider = BuildWorkerServices();
        await using var scope = provider.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(CancellationToken.None)).ShouldBe(3);

        var messages = Consume(3, document.ToString());
        messages.Count.ShouldBe(3);
        messages.Select(m => m.Partition.Value).Distinct().Count().ShouldBe(1);
        messages.Select(m => m.Offset.Value).ShouldBe(messages.Select(m => m.Offset.Value).Order());
    }

    private sealed class ToggledPublisher : IEventPublisher
    {
        public bool Healthy { get; set; }

        public List<OutboxMessage> Published { get; } = [];

        public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            if (!Healthy)
            {
                throw new InvalidOperationException("broker unavailable");
            }

            Published.Add(message);
            return Task.CompletedTask;
        }
    }
}
