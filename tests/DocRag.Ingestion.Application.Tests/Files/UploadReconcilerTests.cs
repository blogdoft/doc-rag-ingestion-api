using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Files;
using DocRag.Ingestion.Application.Options;
using DocRag.Ingestion.Application.Tests.Support;
using DocRag.Ingestion.Domain.Files;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DocRag.Ingestion.Application.Tests.Files;

public class UploadReconcilerTests
{
    private readonly IUploadAttemptRepository _attempts = Substitute.For<IUploadAttemptRepository>();
    private readonly IDocumentVersionRepository _versions = Substitute.For<IDocumentVersionRepository>();
    private readonly IIdempotencyStore _idempotency = Substitute.For<IIdempotencyStore>();
    private readonly IObjectStorage _storage = Substitute.For<IObjectStorage>();
    private readonly FakeClock _clock = new(TestData.Now);

    private UploadReconciler Reconciler => new(
        _attempts, _versions, _idempotency, _storage, _clock,
        Microsoft.Extensions.Options.Options.Create(new ReconciliationOptions { MultipartMaxAgeMinutes = 60 }),
        NullLogger<UploadReconciler>.Instance);

    [Fact]
    public async Task Should_ExpireStaleAttemptsRecoverVersionsAndAbortOldMultipartUploads_When_RunStarts()
    {
        _attempts.ListPendingCleanupAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<UploadAttempt>());

        await Reconciler.RunAsync(CancellationToken.None);

        await _attempts.Received(1).ExpireAllStaleAsync(TestData.Now, Arg.Any<CancellationToken>());
        await _versions.Received(1).RecoverOrphanedUploadingAsync(TestData.Now, Arg.Any<CancellationToken>());
        await _storage.Received(1).AbortStaleMultipartUploadsAsync(TestData.Now.AddMinutes(-60), Arg.Any<CancellationToken>());
        await _idempotency.Received(1).DeleteExpiredAsync(TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_DeleteOrphanObjectsAndMarkThemCleaned_When_AttemptsFailedOrExpired()
    {
        var first = TestData.Attempt(1);
        var second = TestData.Attempt(2);
        _attempts.ListPendingCleanupAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new[] { first, second });

        await Reconciler.RunAsync(CancellationToken.None);

        await _storage.Received(1).DeleteAsync(first.TemporaryObjectKey, Arg.Any<CancellationToken>());
        await _storage.Received(1).DeleteAsync(second.TemporaryObjectKey, Arg.Any<CancellationToken>());
        await _attempts.Received(1).MarkCleanedAsync(1, Arg.Any<CancellationToken>());
        await _attempts.Received(1).MarkCleanedAsync(2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_KeepTheAttemptPendingAndContinue_When_DeletingOneObjectFails()
    {
        var failing = TestData.Attempt(1);
        var healthy = TestData.Attempt(2);
        _attempts.ListPendingCleanupAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new[] { failing, healthy });
        _storage.DeleteAsync(failing.TemporaryObjectKey, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("garage"));

        await Reconciler.RunAsync(CancellationToken.None);

        await _attempts.DidNotReceive().MarkCleanedAsync(1, Arg.Any<CancellationToken>());
        await _attempts.Received(1).MarkCleanedAsync(2, Arg.Any<CancellationToken>());
    }
}
