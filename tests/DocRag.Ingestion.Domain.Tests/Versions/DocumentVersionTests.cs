using DocRag.Ingestion.Domain.Files;
using DocRag.Ingestion.Domain.Versions;
using Shouldly;

namespace DocRag.Ingestion.Domain.Tests.Versions;

public class DocumentVersionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static DocumentVersion NewVersion() => DocumentVersionBuilder.New()
        .WithPublicId(Guid.CreateVersion7(Now))
        .ForDocument(10, Guid.CreateVersion7(Now))
        .WithLabel("4.00")
        .Build(Now).Value;

    private static DocumentFile NewFile(DocumentVersion version) => DocumentFile.Create(
        Guid.CreateVersion7(Now), version.Id, "manual.pdf", "application/pdf", 10, new string('a', 64), "bucket", "key", Now).Value;

    [Fact]
    public void Should_MoveToUploading_When_UploadStartsWithoutActiveAttempt()
    {
        var version = NewVersion();

        var result = version.StartUpload(activeAttemptExists: false, Now);

        result.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(IngestionStatus.Uploading);
    }

    [Fact]
    public void Should_ReturnBadRequest_When_AnotherAttemptIsActive()
    {
        var version = NewVersion();

        var result = version.StartUpload(activeAttemptExists: true, Now);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-upload-in-progress");
        version.Status.ShouldBe(IngestionStatus.AwaitingUpload);
    }

    [Fact]
    public void Should_RecordEventAndBecomeUploaded_When_FileIsRegistered()
    {
        var version = NewVersion();
        version.StartUpload(false, Now);
        var file = NewFile(version);
        var eventId = Guid.CreateVersion7(Now);

        var result = version.MarkUploaded(file, eventId, "corr-1", Now);

        result.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(IngestionStatus.Uploaded);
        version.File.ShouldBe(file);
        var domainEvent = version.Events.ShouldHaveSingleItem();
        domainEvent.EventId.ShouldBe(eventId);
        domainEvent.CorrelationId.ShouldBe("corr-1");
        domainEvent.Sha256.ShouldBe(file.Sha256);
        domainEvent.VersionId.ShouldBe(version.PublicId);
    }

    [Fact]
    public void Should_ReturnBadRequest_When_UploadStartsAfterTheFileWasStored()
    {
        var version = NewVersion();
        version.StartUpload(false, Now);
        version.MarkUploaded(NewFile(version), Guid.CreateVersion7(Now), null, Now);

        var result = version.StartUpload(false, Now);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-version-already-uploaded");
    }

    [Fact]
    public void Should_ReturnBadRequestAndRecordNoEvent_When_MarkedUploadedWithoutStarting()
    {
        var version = NewVersion();

        var result = version.MarkUploaded(NewFile(version), Guid.CreateVersion7(Now), null, Now);

        result.IsFailure.ShouldBeTrue();
        version.Events.ShouldBeEmpty();
    }

    [Fact]
    public void Should_AllowNewAttempt_When_UploadFailed()
    {
        var version = NewVersion();
        version.StartUpload(false, Now);
        version.MarkUploadFailed(Now);

        var result = version.StartUpload(false, Now);

        version.Status.ShouldBe(IngestionStatus.Uploading);
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Should_StayUploaded_When_FailureIsReportedAfterCompletion()
    {
        var version = NewVersion();
        version.StartUpload(false, Now);
        version.MarkUploaded(NewFile(version), Guid.CreateVersion7(Now), null, Now);

        version.MarkUploadFailed(Now);

        version.Status.ShouldBe(IngestionStatus.Uploaded);
    }
}
