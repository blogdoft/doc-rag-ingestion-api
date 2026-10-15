using DocRag.Ingestion.Application.Documents;
using DocRag.Ingestion.Application.Idempotency;
using DocRag.Ingestion.Application.Common;
using DocRag.Ingestion.Application.Tests.Support;
using DocRag.Ingestion.Domain.Documents;
using NSubstitute;
using Shouldly;

namespace DocRag.Ingestion.Application.Tests.Documents.DocumentServiceTests;

public class CreateAsyncTests : BaseDocumentServiceTests
{
    [Fact]
    public async Task Should_InsertAndCommit_When_RequestIsValid()
    {
        var command = TestData.CreateDocumentCommand().Generate();

        var result = await Service.CreateAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Title.ShouldBe(command.Title);
        result.Value.PublicId.ShouldNotBe(Guid.Empty);
        await Documents.Received(1).InsertAsync(result.Value, Arg.Any<CancellationToken>());
        await UnitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnUnprocessableAndNotTouchTheDatabase_When_DomainRejectsTheRequest()
    {
        var command = TestData.CreateDocumentCommand().Generate() with { Language = "not a tag" };

        var result = await Service.CreateAsync(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("422-document-invalid");
        await Documents.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
        await UnitOfWork.DidNotReceive().BeginAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_CompleteTheReservationInTheSameTransaction_When_IdempotencyKeyIsSent()
    {
        var command = TestData.CreateDocumentCommand(idempotencyKey: "key-1").Generate();
        IdempotencyStore.TryReserveAsync(Arg.Any<IdempotencyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyReservation(true, null));

        var result = await Service.CreateAsync(command, CancellationToken.None);

        await IdempotencyStore.Received(1).CompleteAsync(
            Arg.Is<IdempotencyRequest>(r => r.Key == "key-1" && r.Operation == "create-document"),
            Arg.Any<string>(),
            201,
            result.Value.PublicId,
            Arg.Any<CancellationToken>());
        await UnitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReplayOriginalDocumentWithoutInserting_When_KeyWasCompletedWithSameContent()
    {
        var command = TestData.CreateDocumentCommand(idempotencyKey: "key-1").Generate();
        var original = TestData.Document();
        IdempotencyStore.TryReserveAsync(Arg.Any<IdempotencyRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => new IdempotencyReservation(
                false, new IdempotencyRecord(true, call.Arg<IdempotencyRequest>().Fingerprint, original.PublicId)));
        Documents.GetAsync(original.PublicId, Arg.Any<CancellationToken>()).Returns(original);

        var result = await Service.CreateAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(original);
        await Documents.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
        await UnitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnConflict_When_KeyWasUsedWithDifferentContent()
    {
        var command = TestData.CreateDocumentCommand(idempotencyKey: "key-1").Generate();
        IdempotencyStore.TryReserveAsync(Arg.Any<IdempotencyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyReservation(false, new IdempotencyRecord(true, "another-fingerprint", Guid.NewGuid())));

        var result = await Service.CreateAsync(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("409-idempotency-key-reused");
        await Documents.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
    }

    [Fact]
    public async Task Should_ReturnConflict_When_TheSameKeyIsStillBeingProcessed()
    {
        var command = TestData.CreateDocumentCommand(idempotencyKey: "key-1").Generate();
        IdempotencyStore.TryReserveAsync(Arg.Any<IdempotencyRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyReservation(false, new IdempotencyRecord(false, null, null)));

        var result = await Service.CreateAsync(command, CancellationToken.None);

        result.Failure.Code.ShouldBe("409-idempotency-key-in-progress");
    }

    [Fact]
    public async Task Should_GiveEqualRequestsTheSameFingerprint_When_OnlyMetadataKeyOrderDiffers()
    {
        var first = TestData.CreateDocumentCommand(idempotencyKey: "k").Generate() with
        {
            Metadata = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"a":1,"b":2}"""),
        };
        var second = first with { Metadata = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"b":2,"a":1}""") };
        var fingerprints = new List<string?>();
        IdempotencyStore.TryReserveAsync(Arg.Do<IdempotencyRequest>(r => fingerprints.Add(r.Fingerprint)), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyReservation(true, null));

        await Service.CreateAsync(first, CancellationToken.None);
        await Service.CreateAsync(second, CancellationToken.None);

        fingerprints.Distinct().Count().ShouldBe(1);
    }
}
