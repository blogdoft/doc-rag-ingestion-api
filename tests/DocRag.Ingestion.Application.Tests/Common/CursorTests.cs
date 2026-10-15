using DocRag.Ingestion.Application.Common;
using Shouldly;

namespace DocRag.Ingestion.Application.Tests.Common;

public class CursorTests
{
    [Fact]
    public void Should_RoundTripTheIdentifier_When_EncodedAndDecoded()
    {
        var id = Guid.CreateVersion7();

        var decoded = Cursor.Decode(Cursor.Encode(id));

        decoded.IsSuccess.ShouldBeTrue();
        decoded.Value.ShouldBe(id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Should_ReturnNoCursor_When_FirstPageIsRequested(string? cursor)
    {
        var decoded = Cursor.Decode(cursor);

        decoded.IsSuccess.ShouldBeTrue();
        decoded.Value.ShouldBeNull();
    }

    [Theory]
    [InlineData("not base64 !!")]
    [InlineData("AAAA")]
    public void Should_ReturnBadRequest_When_CursorIsMalformed(string cursor)
    {
        var decoded = Cursor.Decode(cursor);

        decoded.IsFailure.ShouldBeTrue();
        decoded.Failure.Code.ShouldBe("400-cursor-invalid");
    }
}
