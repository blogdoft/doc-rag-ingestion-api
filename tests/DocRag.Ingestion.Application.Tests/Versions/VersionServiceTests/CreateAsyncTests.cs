using BlogDoFT.Libs.ResultPattern;
using DocRag.Ingestion.Application.Tests.Support;
using DocRag.Ingestion.Domain.Versions;
using NSubstitute;
using Shouldly;

namespace DocRag.Ingestion.Application.Tests.Versions.VersionServiceTests;

public class CreateAsyncTests : BaseVersionServiceTests
{
    [Fact]
    public async Task Should_InsertVersionAwaitingUpload_When_RequestIsValid()
    {
        var service = Service;

        var result = await service.CreateAsync(Command(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(IngestionStatus.AwaitingUpload);
        result.Value.DocumentPublicId.ShouldBe(Document.PublicId);
        await Versions.Received(1).InsertAsync(result.Value, Arg.Any<CancellationToken>());
        await UnitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_DocumentDoesNotExist()
    {
        var service = Service;
        var command = Command() with { DocumentId = Guid.NewGuid() };

        var result = await service.CreateAsync(command, CancellationToken.None);

        result.Failure.Code.ShouldBe("404-document-not-found");
        await Versions.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
    }

    [Fact]
    public async Task Should_ReturnBadRequestAndNotCommit_When_LabelAlreadyExists()
    {
        var service = Service;
        Versions.InsertAsync(Arg.Any<DocumentVersion>(), Arg.Any<CancellationToken>())
            .Returns(new Failure("400-version-label-exists", "duplicate"));

        var result = await service.CreateAsync(Command(), CancellationToken.None);

        result.Failure.Code.ShouldBe("400-version-label-exists");
        await UnitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_EffectiveRangeIsInverted()
    {
        var service = Service;
        var command = Command() with { EffectiveFrom = TestData.Now, EffectiveTo = TestData.Now.AddDays(-1) };

        var result = await service.CreateAsync(command, CancellationToken.None);

        result.Failure.Code.ShouldBe("400-version-invalid");
    }
}
