namespace DocRag.Ingestion.Application.Files;

/// <summary>Raised from inside a stream read when the body exceeds the configured limit.</summary>
/// <param name="limit">The exceeded limit in bytes.</param>
public sealed class UploadTooLargeException(long limit) : Exception($"The upload exceeds the limit of {limit} bytes.")
{
    /// <summary>Gets the exceeded limit in bytes.</summary>
    public long Limit { get; } = limit;
}
