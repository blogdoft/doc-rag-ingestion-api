using System.ComponentModel.DataAnnotations;

namespace DocRag.Ingestion.Infrastructure.Kafka;

/// <summary>Kafka producer settings, bound from the <c>Kafka</c> configuration section.</summary>
public sealed class KafkaOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Kafka";

    /// <summary>Gets or sets the bootstrap servers.</summary>
    [Required]
    public string BootstrapServers { get; set; } = string.Empty;

    /// <summary>Gets or sets the topic receiving <c>DocumentUploaded</c> events.</summary>
    [Required]
    public string TopicDocumentUploaded { get; set; } = "doc-rag.documents.uploaded.v1";

    /// <summary>Gets or sets the security protocol (Plaintext, Ssl, SaslPlaintext, SaslSsl).</summary>
    public string SecurityProtocol { get; set; } = "Plaintext";

    /// <summary>Gets or sets the SASL mechanism (Plain, ScramSha256, ScramSha512), when SASL is used.</summary>
    public string? SaslMechanism { get; set; }

    /// <summary>Gets or sets the SASL user name.</summary>
    public string? SaslUsername { get; set; }

    /// <summary>Gets or sets the SASL password.</summary>
    public string? SaslPassword { get; set; }

    /// <summary>Gets or sets the per-message delivery timeout in milliseconds.</summary>
    [Range(1000, 600000)]
    public int MessageTimeoutMs { get; set; } = 30000;

    /// <summary>Gets or sets the broker request timeout in milliseconds.</summary>
    [Range(1000, 600000)]
    public int RequestTimeoutMs { get; set; } = 10000;
}
