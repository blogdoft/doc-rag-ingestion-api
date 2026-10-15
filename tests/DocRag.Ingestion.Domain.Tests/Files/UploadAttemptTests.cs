using DocRag.Ingestion.Domain.Files;
using Shouldly;

namespace DocRag.Ingestion.Domain.Tests.Files;

public class UploadAttemptTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static UploadAttempt Started() => UploadAttempt.Start(
        Guid.CreateVersion7(Now), 1, Guid.CreateVersion7(Now), "key", Now, TimeSpan.FromMinutes(30));

    [Fact]
    public void Should_BeActiveWithLease_When_Started()
    {
        var attempt = Started();

        attempt.Status.ShouldBe(UploadAttemptStatus.Active);
        attempt.LeaseExpiresAt.ShouldBe(Now.AddMinutes(30));
    }

    [Fact]
    public void Should_BeStale_When_LeaseHasRunOut()
    {
        var attempt = Started();

        attempt.IsStale(Now.AddMinutes(31)).ShouldBeTrue();
        attempt.IsStale(Now.AddMinutes(1)).ShouldBeFalse();
    }

    [Fact]
    public void Should_Complete_When_Active()
    {
        var attempt = Started();

        var result = attempt.Complete(42);

        result.IsSuccess.ShouldBeTrue();
        attempt.Status.ShouldBe(UploadAttemptStatus.Completed);
        attempt.BytesReceived.ShouldBe(42);
    }

    [Fact]
    public void Should_ReturnFailure_When_CompletingAFailedAttempt()
    {
        var attempt = Started();
        attempt.Fail("storage", 5);

        var result = attempt.Complete(42);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("409-upload-attempt-not-active");
    }

    [Fact]
    public void Should_KeepCompletedStatus_When_FailIsCalledAfterCompletion()
    {
        var attempt = Started();
        attempt.Complete(42);

        attempt.Fail("late", 1);

        attempt.Status.ShouldBe(UploadAttemptStatus.Completed);
        attempt.FailureCode.ShouldBeNull();
    }
}
