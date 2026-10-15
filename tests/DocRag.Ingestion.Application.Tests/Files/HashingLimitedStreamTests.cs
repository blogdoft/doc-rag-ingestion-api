using System.Security.Cryptography;
using DocRag.Ingestion.Application.Files;
using Shouldly;

namespace DocRag.Ingestion.Application.Tests.Files;

public class HashingLimitedStreamTests
{
    [Fact]
    public async Task Should_ComputeSha256AndCount_When_StreamIsReadInSmallChunks()
    {
        var data = RandomNumberGenerator.GetBytes(100_000);
        await using var stream = new HashingLimitedStream(new MemoryStream(data), 1_000_000);

        var buffer = new byte[777];
        while (await stream.ReadAsync(buffer) > 0)
        {
        }

        stream.BytesRead.ShouldBe(data.Length);
        stream.GetDigest().ShouldBe(Convert.ToHexStringLower(SHA256.HashData(data)));
    }

    [Fact]
    public async Task Should_Throw_When_BodyExceedsTheLimit()
    {
        await using var stream = new HashingLimitedStream(new MemoryStream(new byte[100]), 99);

        var read = async () => await stream.CopyToAsync(Stream.Null);

        var exception = await read.ShouldThrowAsync<UploadTooLargeException>();
        exception.Limit.ShouldBe(99);
    }

    [Fact]
    public async Task Should_AcceptBodyExactlyAtTheLimit_When_SizeEqualsLimit()
    {
        await using var stream = new HashingLimitedStream(new MemoryStream(new byte[100]), 100);

        await stream.CopyToAsync(Stream.Null);

        stream.BytesRead.ShouldBe(100);
    }
}
