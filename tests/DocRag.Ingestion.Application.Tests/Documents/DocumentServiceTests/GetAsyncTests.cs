using DocRag.Ingestion.Application.Tests.Support;
using NSubstitute;
using Shouldly;

namespace DocRag.Ingestion.Application.Tests.Documents.DocumentServiceTests;

public class GetAsyncTests : BaseDocumentServiceTests
{
    [Fact]
    public async Task Should_ReturnDocument_When_ItExists()
    {
        var document = TestData.Document();
        Documents.GetAsync(document.PublicId, Arg.Any<CancellationToken>()).Returns(document);

        var result = await Service.GetAsync(document.PublicId, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(document);
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_TheRepositoryFindsNothing()
    {
        var id = Guid.NewGuid();
        Documents.GetAsync(id, Arg.Any<CancellationToken>()).Returns((DocRag.Ingestion.Domain.Documents.Document?)null);

        var result = await Service.GetAsync(id, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("404-document-not-found");
    }
}
