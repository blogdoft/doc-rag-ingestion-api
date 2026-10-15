using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Options;
using DocRag.Ingestion.Application.Outbox;
using DocRag.Ingestion.Application.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace DocRag.Ingestion.Application.Tests.Outbox;

public class OutboxDispatcherTests
{
    private readonly IOutboxRepository _outbox = Substitute.For<IOutboxRepository>();
    private readonly IEventPublisher _publisher = Substitute.For<IEventPublisher>();
    private readonly FakeClock _clock = new(TestData.Now);

    private OutboxDispatcher Dispatcher => new(
        _outbox, _publisher, _clock, Microsoft.Extensions.Options.Options.Create(new OutboxOptions()), NullLogger<OutboxDispatcher>.Instance);

    private static OutboxMessage Message(long id, int attempts = 0) =>
        new(id, Guid.CreateVersion7(), "DocumentUploaded", Guid.NewGuid(), "{}", TestData.Now, attempts);

    private void Claim(params OutboxMessage[] messages) =>
        _outbox.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(messages);

    [Fact]
    public async Task Should_PublishAndMarkEachMessage_When_BrokerAcknowledges()
    {
        Claim(Message(1), Message(2));

        var published = await Dispatcher.DispatchBatchAsync(CancellationToken.None);

        published.ShouldBe(2);
        await _outbox.Received(1).MarkPublishedAsync(1, TestData.Now, Arg.Any<CancellationToken>());
        await _outbox.Received(1).MarkPublishedAsync(2, TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ScheduleRetryAndContinueWithTheNextMessage_When_PublishingOneFails()
    {
        var failing = Message(1);
        var healthy = Message(2);
        Claim(failing, healthy);
        _publisher.PublishAsync(failing, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("broker down"));

        var published = await Dispatcher.DispatchBatchAsync(CancellationToken.None);

        published.ShouldBe(1);
        await _outbox.DidNotReceive().MarkPublishedAsync(1, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _outbox.Received(1).MarkFailedAsync(1, "broker down", TestData.Now.AddSeconds(5), Arg.Any<CancellationToken>());
        await _outbox.Received(1).MarkPublishedAsync(2, TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_NotMarkPublished_When_AckIsLostAfterPublishingSoTheMessageIsRetried()
    {
        var message = Message(1);
        Claim(message);
        _outbox.MarkPublishedAsync(1, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("db"));

        var published = await Dispatcher.DispatchBatchAsync(CancellationToken.None);

        // At-least-once: the event was sent, and stays pending so it is delivered again (consumers dedupe by eventId).
        published.ShouldBe(0);
        await _publisher.Received(1).PublishAsync(message, Arg.Any<CancellationToken>());
        await _outbox.Received(1).MarkFailedAsync(1, Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_PropagateCancellation_When_ShutdownIsRequested()
    {
        Claim(Message(1));
        using var cts = new CancellationTokenSource();
        _publisher.PublishAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>()).Returns<Task>(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        var run = async () => await Dispatcher.DispatchBatchAsync(cts.Token);

        await run.ShouldThrowAsync<OperationCanceledException>();
        await _outbox.DidNotReceiveWithAnyArgs().MarkFailedAsync(default, default!, default, default);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(10, 900)]
    [InlineData(50, 900)]
    public void Should_BackOffExponentiallyUpToTheCap_When_AttemptsGrow(int attempts, int expectedSeconds)
    {
        OutboxDispatcher.Backoff(attempts).TotalSeconds.ShouldBe(expectedSeconds);
    }

    [Fact]
    public void Should_KeyMessagesByDocument_When_ComposingTheKafkaKey()
    {
        var message = Message(1);

        message.Key.ShouldBe(message.AggregateId.ToString());
    }
}
