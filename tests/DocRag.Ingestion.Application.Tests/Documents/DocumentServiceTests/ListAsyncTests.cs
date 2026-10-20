using DocRag.Ingestion.Application.Common;
using DocRag.Ingestion.Application.Documents;
using DocRag.Ingestion.Application.Tests.Support;
using DocRag.Ingestion.Domain.Documents;
using NSubstitute;
using Shouldly;

namespace DocRag.Ingestion.Application.Tests.Documents.DocumentServiceTests;

public class ListAsyncTests : BaseDocumentServiceTests
{
    private static List<Document> Rows(int count) =>
        Enumerable.Range(1, count).Select(i => TestData.Document(id: i)).ToList();

    [Fact]
    public async Task Should_ReturnNextCursorOfTheLastItem_When_MoreRowsExistThanThePageSize()
    {
        var rows = Rows(3);
        Documents.ListAsync(Arg.Any<DocumentFilter>(), 3, null, Arg.Any<CancellationToken>()).Returns(rows);

        var result = await Service.ListAsync(new DocumentFilter(), 2, null, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(2);
        Cursor.Decode(result.Value.NextCursor).Value.ShouldBe(rows[1].PublicId);
    }

    [Fact]
    public async Task Should_ReturnNoCursor_When_ThisIsTheLastPage()
    {
        Documents.ListAsync(Arg.Any<DocumentFilter>(), 3, null, Arg.Any<CancellationToken>()).Returns(Rows(2));

        var result = await Service.ListAsync(new DocumentFilter(), 2, null, CancellationToken.None);

        result.Value.Items.Count.ShouldBe(2);
        result.Value.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task Should_ContinueAfterTheCursor_When_ACursorIsSent()
    {
        var last = Guid.CreateVersion7();
        Documents.ListAsync(Arg.Any<DocumentFilter>(), 11, last, Arg.Any<CancellationToken>()).Returns(Rows(1));

        var result = await Service.ListAsync(new DocumentFilter(), 10, Cursor.Encode(last), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Should_IgnoreBlankFilters_When_TheyAreWhitespace()
    {
        Documents.ListAsync(Arg.Any<DocumentFilter>(), 21, null, Arg.Any<CancellationToken>()).Returns(Rows(0));

        await Service.ListAsync(new DocumentFilter(null, null, "cte", "  "), 20, null, CancellationToken.None);

        await Documents.Received(1).ListAsync(
            Arg.Is<DocumentFilter>(f => f.TypeId == null && f.LanguageId == null && f.Tag == "cte" && f.Title == null),
            21,
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ForwardTypeAndLanguageIds_When_TheyAreInTheFilter()
    {
        Documents.ListAsync(Arg.Any<DocumentFilter>(), 21, null, Arg.Any<CancellationToken>()).Returns(Rows(0));

        await Service.ListAsync(new DocumentFilter(TestData.TypeId, TestData.LanguageId), 20, null, CancellationToken.None);

        await Documents.Received(1).ListAsync(
            Arg.Is<DocumentFilter>(f => f.TypeId == TestData.TypeId && f.LanguageId == TestData.LanguageId),
            21,
            null,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Should_ReturnBadRequest_When_PageSizeIsOutOfRange(int pageSize)
    {
        var result = await Service.ListAsync(new DocumentFilter(), pageSize, null, CancellationToken.None);

        result.Failure.Code.ShouldBe("400-page-size-invalid");
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_CursorIsMalformed()
    {
        var result = await Service.ListAsync(new DocumentFilter(), 10, "%%%", CancellationToken.None);

        result.Failure.Code.ShouldBe("400-cursor-invalid");
    }

    [Fact]
    public async Task Should_TrimTheTitleFilter_When_ItHasSurroundingSpaces()
    {
        Documents.ListAsync(Arg.Any<DocumentFilter>(), 21, null, Arg.Any<CancellationToken>()).Returns(Rows(0));

        await Service.ListAsync(new DocumentFilter(Title: "  manual*  "), 20, null, CancellationToken.None);

        await Documents.Received(1).ListAsync(
            Arg.Is<DocumentFilter>(f => f.Title == "manual*"), 21, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_TheTitleFilterIsLongerThanATitleCanBe()
    {
        var result = await Service.ListAsync(new DocumentFilter(Title: new string('a', 301)), 10, null, CancellationToken.None);

        result.Failure.Code.ShouldBe("400-title-filter-too-long");
        await Documents.DidNotReceiveWithAnyArgs().ListAsync(default!, default, default, default);
    }
}
