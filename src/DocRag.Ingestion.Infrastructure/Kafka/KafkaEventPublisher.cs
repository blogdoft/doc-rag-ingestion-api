using Confluent.Kafka;
using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Common;
using DocRag.Ingestion.Application.Outbox;
using Microsoft.Extensions.Options;

namespace DocRag.Ingestion.Infrastructure.Kafka;

/// <summary>
/// Kafka publisher configured for durability: <c>acks=all</c>, idempotent producer and a bounded
/// delivery timeout. Messages are keyed by the document identifier to keep one document's events ordered.
/// </summary>
public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;

    /// <summary>Initializes a new instance of the <see cref="KafkaEventPublisher"/> class.</summary>
    /// <param name="options">Kafka settings.</param>
    public KafkaEventPublisher(IOptions<KafkaOptions> options)
    {
        var settings = options.Value;
        _topic = settings.TopicDocumentUploaded;

        var config = new ProducerConfig
        {
            BootstrapServers = settings.BootstrapServers,
            ClientId = "doc-rag-ingestion",
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = settings.MessageTimeoutMs,
            RequestTimeoutMs = settings.RequestTimeoutMs,
            SecurityProtocol = Enum.Parse<SecurityProtocol>(settings.SecurityProtocol, ignoreCase: true),
        };

        if (!string.IsNullOrWhiteSpace(settings.SaslMechanism))
        {
            config.SaslMechanism = Enum.Parse<SaslMechanism>(settings.SaslMechanism, ignoreCase: true);
            config.SaslUsername = settings.SaslUsername;
            config.SaslPassword = settings.SaslPassword;
        }

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    /// <inheritdoc />
    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        using var activity = IngestionTelemetry.Activities.StartActivity("kafka.publish", System.Diagnostics.ActivityKind.Producer);
        activity?.SetTag("messaging.destination.name", _topic);

        var kafkaMessage = new Message<string, string>
        {
            Key = message.Key,
            Value = message.Payload,
            Headers =
            [
                new Header("event-id", message.EventId.ToByteArray()),
                new Header("event-type", System.Text.Encoding.UTF8.GetBytes(message.EventType)),
            ],
        };

        await _producer.ProduceAsync(_topic, kafkaMessage, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(10));
        _producer.Dispose();
    }
}
