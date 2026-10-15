using System.Buffers;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocRag.Ingestion.Infrastructure.Garage;

/// <summary>
/// <see cref="IObjectStorage"/> over Garage's S3 API. Streams of unknown length are sent as a
/// multipart upload in fixed-size parts, so memory stays at roughly one part regardless of file
/// size; incomplete multipart uploads are aborted on failure.
/// </summary>
public sealed class GarageObjectStorage : IObjectStorage, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly GarageOptions _options;
    private readonly ILogger<GarageObjectStorage> _logger;

    /// <summary>Initializes a new instance of the <see cref="GarageObjectStorage"/> class.</summary>
    /// <param name="options">Garage settings.</param>
    /// <param name="logger">Logger.</param>
    public GarageObjectStorage(IOptions<GarageOptions> options, ILogger<GarageObjectStorage> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new AmazonS3Client(
            new BasicAWSCredentials(_options.AccessKey, _options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = _options.ServiceUrl,
                AuthenticationRegion = _options.Region,
                ForcePathStyle = _options.ForcePathStyle,

                // Garage does not implement the flexible-checksum trailers newer SDKs send by default.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
    }

    /// <inheritdoc />
    public string Bucket => _options.Bucket;

    /// <inheritdoc />
    public async Task<long> PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        using var activity = IngestionTelemetry.Activities.StartActivity("storage.put");
        var partSize = _options.PartSizeMiB * 1024 * 1024;
        var buffer = ArrayPool<byte>.Shared.Rent(partSize);
        string? uploadId = null;
        try
        {
            var filled = await FillAsync(content, buffer, partSize, cancellationToken);
            if (filled < partSize)
            {
                await _client.PutObjectAsync(
                    new PutObjectRequest
                    {
                        BucketName = Bucket,
                        Key = key,
                        ContentType = contentType,
                        InputStream = new MemoryStream(buffer, 0, filled, writable: false),
                        AutoCloseStream = true,
                        AutoResetStreamPosition = false,
                        UseChunkEncoding = false,
                    },
                    cancellationToken);
                return filled;
            }

            var initiated = await _client.InitiateMultipartUploadAsync(
                new InitiateMultipartUploadRequest { BucketName = Bucket, Key = key, ContentType = contentType }, cancellationToken);
            uploadId = initiated.UploadId;

            var parts = new List<PartETag>();
            long total = 0;
            var partNumber = 1;
            while (filled > 0)
            {
                var uploaded = await _client.UploadPartAsync(
                    new UploadPartRequest
                    {
                        BucketName = Bucket,
                        Key = key,
                        UploadId = uploadId,
                        PartNumber = partNumber,
                        PartSize = filled,
                        InputStream = new MemoryStream(buffer, 0, filled, writable: false),
                        UseChunkEncoding = false,
                    },
                    cancellationToken);
                parts.Add(new PartETag(partNumber, uploaded.ETag));
                total += filled;
                partNumber++;
                filled = await FillAsync(content, buffer, partSize, cancellationToken);
            }

            await _client.CompleteMultipartUploadAsync(
                new CompleteMultipartUploadRequest { BucketName = Bucket, Key = key, UploadId = uploadId, PartETags = parts },
                cancellationToken);
            uploadId = null;
            return total;
        }
        catch
        {
            if (uploadId is not null)
            {
                await AbortQuietlyAsync(key, uploadId);
            }

            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <inheritdoc />
    public async Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var metadata = await _client.GetObjectMetadataAsync(Bucket, key, cancellationToken);
            return metadata.ContentLength;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        await _client.DeleteObjectAsync(Bucket, key, cancellationToken);

    /// <inheritdoc />
    public async Task<int> AbortStaleMultipartUploadsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        var aborted = 0;
        var request = new ListMultipartUploadsRequest { BucketName = Bucket };
        ListMultipartUploadsResponse page;
        do
        {
            page = await _client.ListMultipartUploadsAsync(request, cancellationToken);
            foreach (var upload in (page.MultipartUploads ?? []).Where(u => u.Initiated is { } started && new DateTimeOffset(started.ToUniversalTime()) < olderThan))
            {
                await _client.AbortMultipartUploadAsync(
                    new AbortMultipartUploadRequest { BucketName = Bucket, Key = upload.Key, UploadId = upload.UploadId },
                    cancellationToken);
                aborted++;
            }

            request.KeyMarker = page.NextKeyMarker;
            request.UploadIdMarker = page.NextUploadIdMarker;
        }
        while (page.IsTruncated == true);

        return aborted;
    }

    /// <inheritdoc />
    public async Task EnsureAccessibleAsync(CancellationToken cancellationToken = default) =>
        await _client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = Bucket, MaxKeys = 1 }, cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    private static async Task<int> FillAsync(Stream source, byte[] buffer, int size, CancellationToken cancellationToken)
    {
        var filled = 0;
        while (filled < size)
        {
            var read = await source.ReadAsync(buffer.AsMemory(filled, size - filled), cancellationToken);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        return filled;
    }

    private async Task AbortQuietlyAsync(string key, string uploadId)
    {
        try
        {
            await _client.AbortMultipartUploadAsync(
                new AbortMultipartUploadRequest { BucketName = Bucket, Key = key, UploadId = uploadId }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not abort multipart upload {UploadId}; reconciliation will remove it", uploadId);
        }
    }
}
