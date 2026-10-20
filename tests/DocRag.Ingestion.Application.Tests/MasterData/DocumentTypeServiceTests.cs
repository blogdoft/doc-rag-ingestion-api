using BlogDoFT.Libs.ResultPattern;
using DocRag.Ingestion.Application.Common;
using DocRag.Ingestion.Application.MasterData;
using DocRag.Ingestion.Application.Tests.Support;
using DocRag.Ingestion.Domain.MasterData;
using NSubstitute;
using Shouldly;

namespace DocRag.Ingestion.Application.Tests.MasterData;

public class DocumentTypeServiceTests
{
    private readonly IDocumentTypeRepository _repository = Substitute.For<IDocumentTypeRepository>();
    private readonly FakeClock _clock = new(TestData.Now);

    private DocumentTypeService Service => new(_repository, _clock);

    private static DocumentType Existing(string name = "fiscal-manual", long id = 1)
    {
        var documentType = DocumentType.Builder().WithPublicId(Guid.CreateVersion7(TestData.Now)).Named(name).Build(TestData.Now).Value;
        documentType.AssignId(id);
        return documentType;
    }

    [Fact]
    public async Task Should_InsertAndReturnDocumentType_When_NameIsValid()
    {
        _repository.InsertAsync(Arg.Any<DocumentType>(), Arg.Any<CancellationToken>()).Returns(Result.AsSuccess());

        var result = await Service.CreateAsync("fiscal-manual", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("fiscal-manual");
        result.Value.PublicId.ShouldNotBe(Guid.Empty);
        await _repository.Received(1).InsertAsync(result.Value, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_NotInsert_When_NameIsInvalid()
    {
        var result = await Service.CreateAsync(" ", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-document-type-invalid");
        await _repository.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
    }

    [Fact]
    public async Task Should_ReturnTheRepositoryFailure_When_TheNameIsAlreadyTaken()
    {
        _repository.InsertAsync(Arg.Any<DocumentType>(), Arg.Any<CancellationToken>())
            .Returns(new Failure("400-document-type-name-exists", "taken"));

        var result = await Service.CreateAsync("fiscal-manual", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-document-type-name-exists");
    }

    [Fact]
    public async Task Should_ReturnDocumentType_When_ItExists()
    {
        var existing = Existing();
        _repository.GetAsync(existing.PublicId, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await Service.GetAsync(existing.PublicId, CancellationToken.None);

        result.Value.ShouldBe(existing);
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_GettingAnUnknownDocumentType()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((DocumentType?)null);

        var result = await Service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        result.Failure.Code.ShouldBe("404-document-type-not-found");
    }

    [Fact]
    public async Task Should_ReturnNextCursorOfTheLastItem_When_MoreRowsExistThanThePageSize()
    {
        var rows = new List<DocumentType> { Existing(id: 1), Existing(id: 2), Existing(id: 3) };
        _repository.ListAsync(3, null, Arg.Any<CancellationToken>()).Returns(rows);

        var result = await Service.ListAsync(2, null, CancellationToken.None);

        result.Value.Items.Count.ShouldBe(2);
        Cursor.Decode(result.Value.NextCursor).Value.ShouldBe(rows[1].PublicId);
    }

    [Fact]
    public async Task Should_ReturnNoCursor_When_ThisIsTheLastPage()
    {
        _repository.ListAsync(3, null, Arg.Any<CancellationToken>()).Returns(new List<DocumentType> { Existing() });

        var result = await Service.ListAsync(2, null, CancellationToken.None);

        result.Value.NextCursor.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Should_ReturnFailure_When_PageSizeIsOutOfRange(int pageSize)
    {
        var result = await Service.ListAsync(pageSize, null, CancellationToken.None);

        result.Failure.Code.ShouldBe("400-page-size-invalid");
    }

    [Fact]
    public async Task Should_RenameAndPersist_When_TheDocumentTypeExists()
    {
        var existing = Existing();
        _repository.GetAsync(existing.PublicId, Arg.Any<CancellationToken>()).Returns(existing);
        _repository.UpdateAsync(existing, Arg.Any<CancellationToken>()).Returns(Result.AsSuccess());

        var result = await Service.UpdateAsync(existing.PublicId, "legal-opinion", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("legal-opinion");
        await _repository.Received(1).UpdateAsync(existing, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_UpdatingAnUnknownDocumentType()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((DocumentType?)null);

        var result = await Service.UpdateAsync(Guid.NewGuid(), "x", CancellationToken.None);

        result.Failure.Code.ShouldBe("404-document-type-not-found");
    }

    [Fact]
    public async Task Should_NotPersist_When_TheNewNameIsInvalid()
    {
        var existing = Existing();
        _repository.GetAsync(existing.PublicId, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await Service.UpdateAsync(existing.PublicId, "", CancellationToken.None);

        result.Failure.Code.ShouldBe("400-document-type-invalid");
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Should_ReturnTheRepositoryFailure_When_RenamingToATakenName()
    {
        var existing = Existing();
        _repository.GetAsync(existing.PublicId, Arg.Any<CancellationToken>()).Returns(existing);
        _repository.UpdateAsync(existing, Arg.Any<CancellationToken>())
            .Returns(new Failure("400-document-type-name-exists", "taken"));

        var result = await Service.UpdateAsync(existing.PublicId, "legal-opinion", CancellationToken.None);

        result.Failure.Code.ShouldBe("400-document-type-name-exists");
    }

    [Fact]
    public async Task Should_Succeed_When_DeletingAnExistingDocumentType()
    {
        var id = Guid.NewGuid();
        _repository.DeleteAsync(id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Service.DeleteAsync(id, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_DeletingAnUnknownDocumentType()
    {
        _repository.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await Service.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        result.Failure.Code.ShouldBe("404-document-type-not-found");
    }
}
