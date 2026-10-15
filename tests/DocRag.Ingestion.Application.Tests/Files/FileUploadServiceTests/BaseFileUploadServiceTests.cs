using System.Text;
using BlogDoFT.Libs.ResultPattern;
using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Files;
using DocRag.Ingestion.Application.Idempotency;
using DocRag.Ingestion.Application.Options;
using DocRag.Ingestion.Application.Tests.Support;
using DocRag.Ingestion.Domain.Documents;
using DocRag.Ingestion.Domain.Files;
using DocRag.Ingestion.Domain.Versions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DocRag.Ingestion.Application.Tests.Files.FileUploadServiceTests;

public abstract class BaseFileUploadServiceTests
{
    protected static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\nhello world\n");

    protected BaseFileUploadServiceTests()
    {
        Document = TestData.Document();
        Version = TestData.Version(Document);

        Versions.GetAsync(Document.PublicId, Version.PublicId, true, Arg.Any<CancellationToken>())
            .Returns(_ => Version);
        Attempts.InsertAsync(Arg.Any<UploadAttempt>(), Arg.Any<CancellationToken>()).Returns(Result.AsSuccess());
        Attempts.FinishAsync(Arg.Any<UploadAttempt>(), Arg.Any<CancellationToken>()).Returns(true);
        Storage.Bucket.Returns("bucket");
        Storage.PutAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                if (PutFailuresRemaining-- > 0)
                {
                    throw new HttpRequestException("garage down");
                }

                var sink = new MemoryStream();
                await call.Arg<Stream>().CopyToAsync(sink);
                StoredBytes = sink.ToArray();
                return sink.Length;
            });
        Storage.GetSizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => (long?)StoredBytes?.Length);
    }

    protected Document Document { get; }

    protected DocumentVersion Version { get; }

    protected byte[]? StoredBytes { get; private set; }

    protected int PutFailuresRemaining { get; set; }

    protected UploadOptions Options { get; } = new();

    protected IDocumentVersionRepository Versions { get; } = Substitute.For<IDocumentVersionRepository>();

    protected IFileRepository Files { get; } = Substitute.For<IFileRepository>();

    protected IUploadAttemptRepository Attempts { get; } = Substitute.For<IUploadAttemptRepository>();

    protected IOutboxRepository Outbox { get; } = Substitute.For<IOutboxRepository>();

    protected IIdempotencyStore IdempotencyStore { get; } = Substitute.For<IIdempotencyStore>();

    protected IObjectStorage Storage { get; } = Substitute.For<IObjectStorage>();

    protected IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

    protected FakeClock Clock { get; } = new(TestData.Now);

    protected FileUploadService Service => new(
        Versions,
        Files,
        Attempts,
        Outbox,
        IdempotencyStore,
        new IdempotencyGate(IdempotencyStore, Clock, Microsoft.Extensions.Options.Options.Create(Options)),
        Storage,
        UnitOfWork,
        Clock,
        Microsoft.Extensions.Options.Options.Create(Options),
        NullLogger<FileUploadService>.Instance);

    protected UploadFileCommand Command(
        byte[]? body = null,
        string contentType = "application/pdf",
        string? fileName = "manual.pdf",
        string? idempotencyKey = null,
        string? expectedSha256 = null) => new(
            Document.PublicId,
            Version.PublicId,
            idempotencyKey,
            fileName,
            contentType,
            expectedSha256,
            "corr-1",
            new MemoryStream(body ?? PdfBytes));

    protected static string Sha256Of(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
}
