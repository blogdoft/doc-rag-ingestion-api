using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Documents;
using DocRag.Ingestion.Application.Idempotency;
using DocRag.Ingestion.Application.MasterData;
using DocRag.Ingestion.Application.Options;
using DocRag.Ingestion.Application.Tests.Support;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DocRag.Ingestion.Application.Tests.Documents.DocumentServiceTests;

public abstract class BaseDocumentServiceTests
{
    protected IDocumentRepository Documents { get; } = Substitute.For<IDocumentRepository>();

    protected IDocumentTypeRepository DocumentTypes { get; } = Substitute.For<IDocumentTypeRepository>();

    protected IDocumentLanguageRepository DocumentLanguages { get; } = Substitute.For<IDocumentLanguageRepository>();

    protected IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

    protected IIdempotencyStore IdempotencyStore { get; } = Substitute.For<IIdempotencyStore>();

    protected FakeClock Clock { get; } = new(TestData.Now);

    protected BaseDocumentServiceTests()
    {
        DocumentTypes.GetAsync(TestData.TypeId, Arg.Any<CancellationToken>()).Returns(TestData.DocumentType());
        DocumentLanguages.GetAsync(TestData.LanguageId, Arg.Any<CancellationToken>()).Returns(TestData.DocumentLanguage());
    }

    protected DocumentService Service => new(
        Documents,
        DocumentTypes,
        DocumentLanguages,
        UnitOfWork,
        new IdempotencyGate(IdempotencyStore, Clock, Microsoft.Extensions.Options.Options.Create(new UploadOptions())),
        IdempotencyStore,
        Clock);
}
