using DocRag.Ingestion.Application.Tests.Support;
using DocRag.Ingestion.Domain.Common;
using DocRag.Ingestion.Domain.Documents;
using NSubstitute;
using Shouldly;

namespace DocRag.Ingestion.Application.Tests.Documents.DocumentServiceTests;

public class PatchAsyncTests : BaseDocumentServiceTests
{
    private static DocumentPatch DescriptionPatch => new() { Description = Optional<string?>.Of("updated") };

    [Fact]
    public async Task Should_ReturnBadRequest_When_IfMatchIsMissing()
    {
        var result = await Service.PatchAsync(Guid.NewGuid(), null, DescriptionPatch, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-if-match-required");
        await Documents.DidNotReceiveWithAnyArgs().GetAsync(default, default);
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_RevisionIsStale()
    {
        var document = TestData.Document();
        Documents.GetAsync(document.PublicId, Arg.Any<CancellationToken>()).Returns(document);

        var result = await Service.PatchAsync(document.PublicId, 99, DescriptionPatch, CancellationToken.None);

        result.Failure.Code.ShouldBe("400-etag-mismatch");
        await Documents.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default, default);
    }

    [Fact]
    public async Task Should_UpdateWithTheRevisionThatWasRead_When_EtagMatches()
    {
        var document = TestData.Document();
        Documents.GetAsync(document.PublicId, Arg.Any<CancellationToken>()).Returns(document);
        Documents.UpdateAsync(document, 1, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Service.PatchAsync(document.PublicId, 1, DescriptionPatch, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Description.ShouldBe("updated");
        result.Value.Revision.ShouldBe(2);
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_AnotherWriterWinsTheRace()
    {
        var document = TestData.Document();
        Documents.GetAsync(document.PublicId, Arg.Any<CancellationToken>()).Returns(document);
        Documents.UpdateAsync(document, 1, Arg.Any<CancellationToken>()).Returns(false);

        var result = await Service.PatchAsync(document.PublicId, 1, DescriptionPatch, CancellationToken.None);

        result.Failure.Code.ShouldBe("400-etag-mismatch");
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_DocumentDoesNotExist()
    {
        var id = Guid.NewGuid();
        Documents.GetAsync(id, Arg.Any<CancellationToken>()).Returns((Document?)null);

        var result = await Service.PatchAsync(id, 1, DescriptionPatch, CancellationToken.None);

        result.Failure.Code.ShouldBe("404-document-not-found");
    }

    [Fact]
    public async Task Should_ReturnBadRequestWithoutPersisting_When_PatchViolatesAnInvariant()
    {
        var document = TestData.Document();
        Documents.GetAsync(document.PublicId, Arg.Any<CancellationToken>()).Returns(document);
        var patch = new DocumentPatch { Title = Optional<string?>.Of(string.Empty) };

        var result = await Service.PatchAsync(document.PublicId, 1, patch, CancellationToken.None);

        result.Failure.Code.ShouldBe("400-document-invalid");
        await Documents.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default, default);
    }
}
