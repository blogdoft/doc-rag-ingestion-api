using BlogDoFT.Libs.ResultPattern;
using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Idempotency;
using DocRag.Ingestion.Application.Options;
using DocRag.Ingestion.Application.Tests.Support;
using DocRag.Ingestion.Application.Versions;
using DocRag.Ingestion.Domain.Documents;
using NSubstitute;

namespace DocRag.Ingestion.Application.Tests.Versions.VersionServiceTests;

public abstract class BaseVersionServiceTests
{
    protected IDocumentRepository Documents { get; } = Substitute.For<IDocumentRepository>();

    protected IDocumentVersionRepository Versions { get; } = Substitute.For<IDocumentVersionRepository>();

    protected IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

    protected IIdempotencyStore IdempotencyStore { get; } = Substitute.For<IIdempotencyStore>();

    protected FakeClock Clock { get; } = new(TestData.Now);

    protected Document Document { get; } = TestData.Document();

    protected VersionService Service
    {
        get
        {
            Documents.GetAsync(Document.PublicId, Arg.Any<CancellationToken>()).Returns(Document);
            Versions.InsertAsync(Arg.Any<DocRag.Ingestion.Domain.Versions.DocumentVersion>(), Arg.Any<CancellationToken>())
                .Returns(Result.AsSuccess());
            return new VersionService(
                Documents,
                Versions,
                UnitOfWork,
                new IdempotencyGate(IdempotencyStore, Clock, Microsoft.Extensions.Options.Options.Create(new UploadOptions())),
                IdempotencyStore,
                Clock);
        }
    }

    protected CreateVersionCommand Command(string label = "4.00") =>
        new(Document.PublicId, null, label, null, null, null);
}
