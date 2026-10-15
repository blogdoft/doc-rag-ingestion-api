using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Outbox;
using DocRag.Ingestion.Infrastructure.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace DocRag.Ingestion.Infrastructure.Tests.Postgres;

[Collection(InfrastructureCollection.Name)]
public class OutboxAndTransactionTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private ServiceProvider _provider = null!;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _connectionString = await infrastructure.NewDatabaseAsync();
        _provider = ServiceHost.Build(_connectionString, infrastructure.GarageServiceUrl, "unused");
        await ServiceHost.MigrateAsync(_provider);
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private static OutboxMessage Message() => new(0, Guid.CreateVersion7(), "DocumentUploaded", Guid.NewGuid(), """{"a":1}""", Now, 0);

    private async Task<long> CountAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task SeedAsync(int count)
    {
        await using var scope = _provider.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        await unitOfWork.BeginAsync();
        for (var i = 0; i < count; i++)
        {
            await outbox.AddAsync(Message());
        }

        await unitOfWork.CommitAsync();
    }

    [Fact]
    public async Task Should_LeaveNoOutboxMessage_When_TheTransactionRollsBack()
    {
        await using (var scope = _provider.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await unitOfWork.BeginAsync();
            await scope.ServiceProvider.GetRequiredService<IOutboxRepository>().AddAsync(Message());
            await unitOfWork.RollbackAsync();
        }

        (await CountAsync("SELECT count(*) FROM outbox_messages")).ShouldBe(0);
    }

    [Fact]
    public async Task Should_LeaveNoOutboxMessage_When_AScopeIsDisposedWithoutCommitting()
    {
        await using (var scope = _provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();
            await scope.ServiceProvider.GetRequiredService<IOutboxRepository>().AddAsync(Message());
        }

        (await CountAsync("SELECT count(*) FROM outbox_messages")).ShouldBe(0);
    }

    [Fact]
    public async Task Should_GiveDisjointBatchesToConcurrentDispatchers_When_MessagesAreClaimedWithSkipLocked()
    {
        await SeedAsync(60);

        async Task<IReadOnlyList<OutboxMessage>> ClaimAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IOutboxRepository>().ClaimBatchAsync(25, TimeSpan.FromMinutes(2), Now);
        }

        var claims = await Task.WhenAll(ClaimAsync(), ClaimAsync(), ClaimAsync());

        var ids = claims.SelectMany(c => c).Select(m => m.Id).ToList();
        ids.Count.ShouldBe(ids.Distinct().Count());
        ids.Count.ShouldBe(60);
        claims.ShouldAllBe(c => c.Count <= 25);
    }

    [Fact]
    public async Task Should_NotReclaimMessagesBeforeTheLeaseExpires_And_ReclaimThemAfterwards()
    {
        await SeedAsync(3);
        await using var scope = _provider.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        await outbox.ClaimBatchAsync(10, TimeSpan.FromMinutes(2), Now);

        var during = await outbox.ClaimBatchAsync(10, TimeSpan.FromMinutes(2), Now.AddMinutes(1));
        var after = await outbox.ClaimBatchAsync(10, TimeSpan.FromMinutes(2), Now.AddMinutes(3));

        during.ShouldBeEmpty();
        after.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Should_HideMessagesUntilTheirRetryTime_When_DeliveryFailed()
    {
        await SeedAsync(1);
        await using var scope = _provider.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var claimed = (await outbox.ClaimBatchAsync(10, TimeSpan.FromMinutes(2), Now)).Single();

        await outbox.MarkFailedAsync(claimed.Id, "boom", Now.AddMinutes(10));

        (await outbox.ClaimBatchAsync(10, TimeSpan.FromMinutes(2), Now.AddMinutes(5))).ShouldBeEmpty();
        var retry = (await outbox.ClaimBatchAsync(10, TimeSpan.FromMinutes(2), Now.AddMinutes(11))).Single();
        retry.Attempts.ShouldBe(1);
        retry.EventId.ShouldBe(claimed.EventId);
    }

    [Fact]
    public async Task Should_ReportBacklogSizeAndAge_When_MessagesArePending()
    {
        await SeedAsync(4);
        await using var scope = _provider.CreateAsyncScope();

        var backlog = await scope.ServiceProvider.GetRequiredService<IOutboxRepository>().GetBacklogAsync(Now.AddMinutes(30));

        backlog.Pending.ShouldBe(4);
        backlog.OldestAge.ShouldBe(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task Should_NeverReturnPublishedMessages_When_TheyWereMarkedPublished()
    {
        await SeedAsync(2);
        await using var scope = _provider.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var claimed = await outbox.ClaimBatchAsync(10, TimeSpan.FromMinutes(2), Now);
        await outbox.MarkPublishedAsync(claimed[0].Id, Now);

        var later = await outbox.ClaimBatchAsync(10, TimeSpan.FromMinutes(2), Now.AddHours(1));

        later.Select(m => m.Id).ShouldBe([claimed[1].Id]);
    }
}
