using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Documents;
using DocRag.Ingestion.Application.Idempotency;
using DocRag.Ingestion.Application.Options;
using DocRag.Ingestion.Application.Tests.Support;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DocRag.Ingestion.Application.Tests.Documents.DocumentServiceTests;

public abstract class BaseDocumentServiceTests
{
    protected IDocumentRepository Documents { get; } = Substitute.For<IDocumentRepository>();

    protected IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

    protected IIdempotencyStore IdempotencyStore { get; } = Substitute.For<IIdempotencyStore>();

    protected FakeClock Clock { get; } = new(TestData.Now);

    protected DocumentService Service => new(
        Documents,
        UnitOfWork,
        new IdempotencyGate(IdempotencyStore, Clock, Microsoft.Extensions.Options.Options.Create(new UploadOptions())),
        IdempotencyStore,
        Clock);
}
