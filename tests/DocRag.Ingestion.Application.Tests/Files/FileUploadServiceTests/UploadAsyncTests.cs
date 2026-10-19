using BlogDoFT.Libs.ResultPattern;
using DocRag.Ingestion.Application.Idempotency;
using DocRag.Ingestion.Application.Outbox;
using DocRag.Ingestion.Application.Tests.Support;
using DocRag.Ingestion.Domain.Files;
using DocRag.Ingestion.Domain.Versions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace DocRag.Ingestion.Application.Tests.Files.FileUploadServiceTests;

public class UploadAsyncTests : BaseFileUploadServiceTests
{
    [Fact]
    public async Task Should_StoreFileAndRegisterOutboxInOneCommit_When_UploadSucceeds()
    {
        var result = await Service.UploadAsync(Command(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Sha256.ShouldBe(Sha256Of(PdfBytes));
        result.Value.SizeBytes.ShouldBe(PdfBytes.Length);
        result.Value.ContentType.ShouldBe("application/pdf");
        StoredBytes.ShouldBe(PdfBytes);
        Version.Status.ShouldBe(IngestionStatus.Uploaded);
        await Files.Received(1).InsertAsync(result.Value, Arg.Any<CancellationToken>());
        await Outbox.Received(1).AddAsync(
            Arg.Is<OutboxMessage>(m => m.EventType == "DocumentUploaded" && m.AggregateId == Document.PublicId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_PersistFileVersionAndOutboxInTheSameTransaction_When_UploadSucceeds()
    {
        await Service.UploadAsync(Command(), CancellationToken.None);

        // Two transactions: admission, then the finalizing one that holds file + status + outbox.
        await UnitOfWork.Received(2).BeginAsync(Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            UnitOfWork.BeginAsync(Arg.Any<CancellationToken>());
            Attempts.InsertAsync(Arg.Any<UploadAttempt>(), Arg.Any<CancellationToken>());
            UnitOfWork.CommitAsync(Arg.Any<CancellationToken>());
            Storage.PutAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            UnitOfWork.BeginAsync(Arg.Any<CancellationToken>());
            Files.InsertAsync(Arg.Any<DocumentFile>(), Arg.Any<CancellationToken>());
            Outbox.AddAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
            UnitOfWork.CommitAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Should_UseAnOpaqueServerGeneratedKey_When_StoringTheObject()
    {
        var result = await Service.UploadAsync(Command(fileName: "../../evil name.pdf"), CancellationToken.None);

        result.Value.OriginalName.ShouldBe("evil name.pdf");
        result.Value.ObjectKey.ShouldBe($"{Document.PublicId}/{Version.PublicId}/{result.Value.PublicId}");
        result.Value.ObjectKey.ShouldNotContain("evil");
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_ContentTypeIsNotAllowed()
    {
        var result = await Service.UploadAsync(Command(contentType: "application/zip"), CancellationToken.None);

        result.Failure.Code.ShouldBe("400-content-type-not-allowed");
        await UnitOfWork.DidNotReceive().BeginAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_FileNameIsMissing()
    {
        var result = await Service.UploadAsync(Command(fileName: null), CancellationToken.None);

        result.Failure.Code.ShouldBe("400-file-name-invalid");
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_VersionDoesNotExist()
    {
        Versions.GetAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), true, Arg.Any<CancellationToken>())
            .Returns((DocumentVersion?)null);

        var result = await Service.UploadAsync(Command(), CancellationToken.None);

        result.Failure.Code.ShouldBe("404-version-not-found");
        await Storage.DidNotReceiveWithAnyArgs().PutAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Should_RejectBeforeTouchingStorageAndFailTheAttempt_When_BodyIsEmpty()
    {
        var result = await Service.UploadAsync(Command(body: []), CancellationToken.None);

        result.Failure.Code.ShouldBe("400-file-empty");
        await Storage.DidNotReceiveWithAnyArgs().PutAsync(default!, default!, default!, default);
        await Attempts.Received(1).FinishAsync(Arg.Is<UploadAttempt>(a => a.Status == UploadAttemptStatus.Failed), Arg.Any<CancellationToken>());
        Version.Status.ShouldBe(IngestionStatus.UploadFailed);
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_SignatureDoesNotMatchTheDeclaredType()
    {
        var result = await Service.UploadAsync(Command(body: "<html>not a pdf</html>"u8.ToArray()), CancellationToken.None);

        result.Failure.Code.ShouldBe("400-file-signature-mismatch");
        await Storage.DidNotReceiveWithAnyArgs().PutAsync(default!, default!, default!, default);
        await Outbox.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Should_ReturnBadRequestAndDeleteTheObject_When_BodyExceedsTheLimit()
    {
        Options.MaxBytes = 10;

        var result = await Service.UploadAsync(Command(), CancellationToken.None);

        result.Failure.Code.ShouldBe("400-file-too-large");
        await Storage.Received(1).DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await Outbox.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await Files.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
    }

    [Fact]
    public async Task Should_ReturnBadRequestAndRemoveTheObject_When_Sha256DiffersFromTheAnnouncedOne()
    {
        var result = await Service.UploadAsync(Command(expectedSha256: new string('0', 64)), CancellationToken.None);

        result.Failure.Code.ShouldBe("400-checksum-mismatch");
        await Storage.Received(1).DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await Outbox.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Should_AcceptUppercaseChecksum_When_ItMatches()
    {
        var result = await Service.UploadAsync(Command(expectedSha256: Sha256Of(PdfBytes).ToUpperInvariant()), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_NotMarkUploadedNorCreateEvent_When_StorageFails()
    {
        PutFailuresRemaining = 1;

        var result = await Service.UploadAsync(Command(), CancellationToken.None);

        result.Failure.Code.ShouldBe("503-storage-unavailable");
        Version.Status.ShouldBe(IngestionStatus.UploadFailed);
        await Files.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
        await Outbox.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Should_AllowANewAttempt_When_ThePreviousOneFailedInStorage()
    {
        PutFailuresRemaining = 1;
        var failed = await Service.UploadAsync(Command(), CancellationToken.None);

        var retry = await Service.UploadAsync(Command(), CancellationToken.None);

        failed.IsFailure.ShouldBeTrue();
        retry.IsSuccess.ShouldBeTrue();
        Version.Status.ShouldBe(IngestionStatus.Uploaded);
    }

    [Fact]
    public async Task Should_ReturnBadRequestWithoutStoring_When_AnotherUploadIsActive()
    {
        Attempts.HasActiveAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Service.UploadAsync(Command(), CancellationToken.None);

        result.Failure.Code.ShouldBe("400-upload-in-progress");
        await Storage.DidNotReceiveWithAnyArgs().PutAsync(default!, default!, default!, default);
        await UnitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnTooManyRequests_When_TheServiceReachedItsConcurrentLimit()
    {
        Attempts.CountActiveAsync(Arg.Any<CancellationToken>()).Returns(Options.MaxConcurrentUploads);

        var result = await Service.UploadAsync(Command(), CancellationToken.None);

        result.Failure.Code.ShouldBe("429-too-many-uploads");
        await Storage.DidNotReceiveWithAnyArgs().PutAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Should_ReturnBadRequestAndNeverRegister_When_TheLeaseExpiredBeforeCompletion()
    {
        Attempts.FinishAsync(Arg.Is<UploadAttempt>(a => a.Status == UploadAttemptStatus.Completed), Arg.Any<CancellationToken>()).Returns(false);

        var result = await Service.UploadAsync(Command(), CancellationToken.None);

        result.Failure.Code.ShouldBe("400-upload-attempt-not-active");
        await Files.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
        await Outbox.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await Storage.Received(1).DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReserveTheKeyBeforeReadingBytesAndCompleteItWithTheDigest_When_IdempotencyKeyIsSent()
    {
        IdempotencyStore.TryReserveAsync(Arg.Any<IdempotencyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyReservation(true, null));

        var result = await Service.UploadAsync(Command(idempotencyKey: "up-1"), CancellationToken.None);

        Received.InOrder(() =>
        {
            IdempotencyStore.TryReserveAsync(Arg.Any<IdempotencyRequest>(), Arg.Any<CancellationToken>());
            Storage.PutAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            IdempotencyStore.CompleteAsync(Arg.Any<IdempotencyRequest>(), Arg.Any<string>(), 201, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        });
        await IdempotencyStore.Received(1).CompleteAsync(
            Arg.Is<IdempotencyRequest>(r => r.Operation == $"upload-file:{Document.PublicId}:{Version.PublicId}"),
            Sha256Of(PdfBytes),
            201,
            result.Value.PublicId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReleaseTheKeySoTheClientCanRetry_When_TheUploadFails()
    {
        IdempotencyStore.TryReserveAsync(Arg.Any<IdempotencyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyReservation(true, null));
        Storage.PutAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("garage down"));

        await Service.UploadAsync(Command(idempotencyKey: "up-1"), CancellationToken.None);

        await IdempotencyStore.Received(1).ReleaseAsync(Arg.Is<IdempotencyRequest>(r => r.Key == "up-1"), Arg.Any<CancellationToken>());
        await IdempotencyStore.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default!, default, default, default);
    }

    [Fact]
    public async Task Should_ReturnTheOriginalFileWithoutStoringAgain_When_KeyWasCompletedWithTheSameBytes()
    {
        var original = TestData.File(Version, Sha256Of(PdfBytes));
        IdempotencyStore.TryReserveAsync(Arg.Any<IdempotencyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyReservation(false, new IdempotencyRecord(true, original.Sha256, original.PublicId)));
        Files.GetAsync(original.PublicId, Arg.Any<CancellationToken>()).Returns(original);

        var result = await Service.UploadAsync(Command(idempotencyKey: "up-1"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(original);
        await Storage.DidNotReceiveWithAnyArgs().PutAsync(default!, default!, default!, default);
        await Outbox.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Should_ReturnBadRequestAndNoEvent_When_KeyWasCompletedWithDifferentBytes()
    {
        var original = TestData.File(Version, new string('c', 64));
        IdempotencyStore.TryReserveAsync(Arg.Any<IdempotencyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyReservation(false, new IdempotencyRecord(true, original.Sha256, original.PublicId)));
        Files.GetAsync(original.PublicId, Arg.Any<CancellationToken>()).Returns(original);

        var result = await Service.UploadAsync(Command(idempotencyKey: "up-1"), CancellationToken.None);

        result.Failure.Code.ShouldBe("400-idempotency-key-reused");
        await Outbox.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Should_RecordFailureAndRethrow_When_TheClientCancelsDuringTheTransfer()
    {
        using var cts = new CancellationTokenSource();
        Storage.PutAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<long>>(_ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        var upload = async () => await Service.UploadAsync(Command(), cts.Token);

        await upload.ShouldThrowAsync<OperationCanceledException>();
        Version.Status.ShouldBe(IngestionStatus.UploadFailed);
        await Outbox.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await Storage.Received().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
