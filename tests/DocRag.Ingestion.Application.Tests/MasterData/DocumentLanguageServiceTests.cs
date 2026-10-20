using BlogDoFT.Libs.ResultPattern;
using DocRag.Ingestion.Application.Common;
using DocRag.Ingestion.Application.MasterData;
using DocRag.Ingestion.Application.Tests.Support;
using DocRag.Ingestion.Domain.MasterData;
using NSubstitute;
using Shouldly;

namespace DocRag.Ingestion.Application.Tests.MasterData;

public class DocumentLanguageServiceTests
{
    private readonly IDocumentLanguageRepository _repository = Substitute.For<IDocumentLanguageRepository>();
    private readonly FakeClock _clock = new(TestData.Now);

    private DocumentLanguageService Service => new(_repository, _clock);

    private static DocumentLanguage Existing(string code = "pt-BR", long id = 1)
    {
        var documentLanguage = DocumentLanguage.Builder().WithPublicId(Guid.CreateVersion7(TestData.Now)).WithCode(code).Build(TestData.Now).Value;
        documentLanguage.AssignId(id);
        return documentLanguage;
    }

    [Fact]
    public async Task Should_InsertAndReturnDocumentLanguage_When_CodeIsValid()
    {
        _repository.InsertAsync(Arg.Any<DocumentLanguage>(), Arg.Any<CancellationToken>()).Returns(Result.AsSuccess());

        var result = await Service.CreateAsync("pt-BR", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Code.ShouldBe("pt-BR");
        result.Value.PublicId.ShouldNotBe(Guid.Empty);
        await _repository.Received(1).InsertAsync(result.Value, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_NotInsert_When_CodeIsInvalid()
    {
        var result = await Service.CreateAsync("nope!", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-document-language-invalid");
        await _repository.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
    }

    [Fact]
    public async Task Should_ReturnTheRepositoryFailure_When_TheCodeIsAlreadyTaken()
    {
        _repository.InsertAsync(Arg.Any<DocumentLanguage>(), Arg.Any<CancellationToken>())
            .Returns(new Failure("400-document-language-code-exists", "taken"));

        var result = await Service.CreateAsync("pt-BR", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-document-language-code-exists");
    }

    [Fact]
    public async Task Should_ReturnDocumentLanguage_When_ItExists()
    {
        var existing = Existing();
        _repository.GetAsync(existing.PublicId, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await Service.GetAsync(existing.PublicId, CancellationToken.None);

        result.Value.ShouldBe(existing);
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_GettingAnUnknownDocumentLanguage()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((DocumentLanguage?)null);

        var result = await Service.GetAsync(Guid.NewGuid(), CancellationToken.None);

        result.Failure.Code.ShouldBe("404-document-language-not-found");
    }

    [Fact]
    public async Task Should_ReturnNextCursorOfTheLastItem_When_MoreRowsExistThanThePageSize()
    {
        var rows = new List<DocumentLanguage> { Existing(id: 1), Existing(id: 2), Existing(id: 3) };
        _repository.ListAsync(3, null, Arg.Any<CancellationToken>()).Returns(rows);

        var result = await Service.ListAsync(2, null, CancellationToken.None);

        result.Value.Items.Count.ShouldBe(2);
        Cursor.Decode(result.Value.NextCursor).Value.ShouldBe(rows[1].PublicId);
    }

    [Fact]
    public async Task Should_ReturnNoCursor_When_ThisIsTheLastPage()
    {
        _repository.ListAsync(3, null, Arg.Any<CancellationToken>()).Returns(new List<DocumentLanguage> { Existing() });

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
    public async Task Should_ChangeAndPersist_When_TheDocumentLanguageExists()
    {
        var existing = Existing();
        _repository.GetAsync(existing.PublicId, Arg.Any<CancellationToken>()).Returns(existing);
        _repository.UpdateAsync(existing, Arg.Any<CancellationToken>()).Returns(Result.AsSuccess());

        var result = await Service.UpdateAsync(existing.PublicId, "en-US", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Code.ShouldBe("en-US");
        await _repository.Received(1).UpdateAsync(existing, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_UpdatingAnUnknownDocumentLanguage()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((DocumentLanguage?)null);

        var result = await Service.UpdateAsync(Guid.NewGuid(), "en", CancellationToken.None);

        result.Failure.Code.ShouldBe("404-document-language-not-found");
    }

    [Fact]
    public async Task Should_NotPersist_When_TheNewCodeIsInvalid()
    {
        var existing = Existing();
        _repository.GetAsync(existing.PublicId, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await Service.UpdateAsync(existing.PublicId, "nope!", CancellationToken.None);

        result.Failure.Code.ShouldBe("400-document-language-invalid");
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Should_ReturnTheRepositoryFailure_When_ChangingToATakenCode()
    {
        var existing = Existing();
        _repository.GetAsync(existing.PublicId, Arg.Any<CancellationToken>()).Returns(existing);
        _repository.UpdateAsync(existing, Arg.Any<CancellationToken>())
            .Returns(new Failure("400-document-language-code-exists", "taken"));

        var result = await Service.UpdateAsync(existing.PublicId, "en-US", CancellationToken.None);

        result.Failure.Code.ShouldBe("400-document-language-code-exists");
    }

    [Fact]
    public async Task Should_Succeed_When_DeletingAnExistingDocumentLanguage()
    {
        var id = Guid.NewGuid();
        _repository.DeleteAsync(id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Service.DeleteAsync(id, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_DeletingAnUnknownDocumentLanguage()
    {
        _repository.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await Service.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        result.Failure.Code.ShouldBe("404-document-language-not-found");
    }
}
