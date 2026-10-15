using DocRag.Ingestion.Application.Files;
using Shouldly;

namespace DocRag.Ingestion.Application.Tests.Files;

public class PrefixedStreamTests
{
    [Fact]
    public async Task Should_ReplayPrefixThenRest_When_ReadToEnd()
    {
        await using var stream = new PrefixedStream(new byte[] { 1, 2, 3 }, new MemoryStream([4, 5]));
        var output = new MemoryStream();

        await stream.CopyToAsync(output);

        output.ToArray().ShouldBe([1, 2, 3, 4, 5]);
    }

    [Fact]
    public async Task Should_ReturnPrefixInPieces_When_BufferIsSmallerThanPrefix()
    {
        await using var stream = new PrefixedStream(new byte[] { 1, 2, 3 }, new MemoryStream([4]));
        var buffer = new byte[2];

        var first = await stream.ReadAsync(buffer);

        first.ShouldBe(2);
        buffer.ShouldBe([1, 2]);
    }
}
