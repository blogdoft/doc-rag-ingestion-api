using DocRag.Ingestion.Application.Common;
using DocRag.Ingestion.Application.Tests.Support;
using DocRag.Ingestion.Domain.Versions;
using NSubstitute;
using Shouldly;

namespace DocRag.Ingestion.Application.Tests.Versions.VersionServiceTests;

public class ListAsyncTests : BaseVersionServiceTests
{
    private List<DocumentVersion> Rows(int count) =>
        Enumerable.Range(1, count).Select(i => TestData.Version(Document, $"v{i}", i)).ToList();

    [Fact]
    public async Task Should_ReturnNextCursorOfTheLastItem_When_MoreRowsExistThanThePageSize()
    {
        var service = Service;
        var rows = Rows(3);
        Versions.ListAsync(Document.Id, 3, null, Arg.Any<CancellationToken>()).Returns(rows);

        var result = await service.ListAsync(Document.PublicId, 2, null, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(2);
        Cursor.Decode(result.Value.NextCursor).Value.ShouldBe(rows[1].PublicId);
    }

    [Fact]
    public async Task Should_ReturnNoCursor_When_ThisIsTheLastPage()
    {
        var service = Service;
        Versions.ListAsync(Document.Id, 3, null, Arg.Any<CancellationToken>()).Returns(Rows(2));

        var result = await service.ListAsync(Document.PublicId, 2, null, CancellationToken.None);

        result.Value.Items.Count.ShouldBe(2);
        result.Value.NextCursor.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Should_ReturnBadRequest_When_PageSizeIsOutOfRange(int pageSize)
    {
        var service = Service;

        var result = await service.ListAsync(Document.PublicId, pageSize, null, CancellationToken.None);

        result.Failure.Code.ShouldBe("400-page-size-invalid");
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_CursorIsMalformed()
    {
        var service = Service;

        var result = await service.ListAsync(Document.PublicId, 10, "%%%", CancellationToken.None);

        result.Failure.Code.ShouldBe("400-cursor-invalid");
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_DocumentDoesNotExist()
    {
        var service = Service;

        var result = await service.ListAsync(Guid.NewGuid(), 10, null, CancellationToken.None);

        result.Failure.Code.ShouldBe("404-document-not-found");
    }
}
