using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Infrastructure.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace DocRag.Ingestion.Infrastructure.Tests.Storage;

[Collection(InfrastructureCollection.Name)]
public class GarageObjectStorageTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private IObjectStorage _storage = null!;
    private AmazonS3Client _s3 = null!;
    private string _bucket = string.Empty;

    public async Task InitializeAsync()
    {
        _bucket = await infrastructure.NewBucketAsync();
        _provider = ServiceHost.Build("Host=unused", infrastructure.GarageServiceUrl, _bucket);
        _storage = _provider.GetRequiredService<IObjectStorage>();
        _s3 = new AmazonS3Client(
            new BasicAWSCredentials(InfrastructureFixture.GarageAccessKey, InfrastructureFixture.GarageSecretKey),
            new AmazonS3Config
            {
                ServiceURL = infrastructure.GarageServiceUrl,
                AuthenticationRegion = "garage",
                ForcePathStyle = true,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
    }

    public async Task DisposeAsync()
    {
        _s3.Dispose();
        await _provider.DisposeAsync();
    }

    private async Task<byte[]> ReadAsync(string key)
    {
        using var response = await _s3.GetObjectAsync(_bucket, key);
        using var memory = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memory);
        return memory.ToArray();
    }

    [Fact]
    public async Task Should_StoreSmallObjectsWithASinglePut_When_StreamFitsInOnePart()
    {
        var data = new byte[100_000];
        Random.Shared.NextBytes(data);

        var stored = await _storage.PutAsync("small", new MemoryStream(data), "application/pdf");

        stored.ShouldBe(data.Length);
        (await ReadAsync("small")).ShouldBe(data);
        (await _storage.GetSizeAsync("small")).ShouldBe(data.Length);
    }

    [Theory]
    [InlineData(5 * 1024 * 1024)] // exactly one part: still a valid object
    [InlineData((12 * 1024 * 1024) + 123)] // three parts, the last one short
    [InlineData(15 * 1024 * 1024)] // an exact multiple of the part size
    public async Task Should_StoreLargeObjectsAsMultipart_When_StreamExceedsOnePart(int size)
    {
        var data = new byte[size];
        Random.Shared.NextBytes(data);

        var stored = await _storage.PutAsync($"large-{size}", new NonSeekableStream(data), "application/pdf");

        stored.ShouldBe(size);
        (await ReadAsync($"large-{size}")).ShouldBe(data);
    }

    [Fact]
    public async Task Should_AbortTheMultipartUploadAndStoreNothing_When_TheSourceFailsMidStream()
    {
        var failing = new FailingStream(7 * 1024 * 1024);

        var put = async () => await _storage.PutAsync("broken", failing, "application/pdf");

        await put.ShouldThrowAsync<IOException>();
        (await _storage.GetSizeAsync("broken")).ShouldBeNull();
        ((await _s3.ListMultipartUploadsAsync(new ListMultipartUploadsRequest { BucketName = _bucket })).MultipartUploads ?? []).ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_ReturnNullSize_When_TheObjectDoesNotExist()
    {
        (await _storage.GetSizeAsync("missing")).ShouldBeNull();
    }

    [Fact]
    public async Task Should_SucceedAndBeIdempotent_When_DeletingTwiceOrDeletingMissingObjects()
    {
        await _storage.PutAsync("to-delete", new MemoryStream([1, 2, 3]), "text/plain");

        await _storage.DeleteAsync("to-delete");
        await _storage.DeleteAsync("to-delete");
        await _storage.DeleteAsync("never-existed");

        (await _storage.GetSizeAsync("to-delete")).ShouldBeNull();
    }

    [Fact]
    public async Task Should_AbortAbandonedMultipartUploads_When_TheyAreOlderThanTheCutoff()
    {
        var initiated = await _s3.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest { BucketName = _bucket, Key = "abandoned" });

        var tooEarly = await _storage.AbortStaleMultipartUploadsAsync(DateTimeOffset.UtcNow.AddHours(-1));
        var aborted = await _storage.AbortStaleMultipartUploadsAsync(DateTimeOffset.UtcNow.AddHours(1));

        tooEarly.ShouldBe(0);
        aborted.ShouldBe(1);
        ((await _s3.ListMultipartUploadsAsync(new ListMultipartUploadsRequest { BucketName = _bucket })).MultipartUploads ?? []).ShouldBeEmpty();
        initiated.UploadId.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Should_VerifyBucketAccess_When_TheBucketExists()
    {
        await Should.NotThrowAsync(async () => await _storage.EnsureAccessibleAsync());
    }

    [Fact]
    public async Task Should_NotCreateTheBucketImplicitly_When_ItDoesNotExist()
    {
        await using var provider = ServiceHost.Build("Host=unused", infrastructure.GarageServiceUrl, "bucket-that-does-not-exist");

        var check = async () => await provider.GetRequiredService<IObjectStorage>().EnsureAccessibleAsync();

        await check.ShouldThrowAsync<AmazonS3Exception>();
    }

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            // Hands out small slices, like a network stream does.
            var take = Math.Min(Math.Min(count, 64 * 1024), data.Length - _position);
            Array.Copy(data, _position, buffer, offset, take);
            _position += take;
            return take;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FailingStream(int failAfter) : Stream
    {
        private int _served;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_served >= failAfter)
            {
                throw new IOException("connection reset by peer");
            }

            var take = Math.Min(count, 64 * 1024);
            _served += take;
            return take;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
