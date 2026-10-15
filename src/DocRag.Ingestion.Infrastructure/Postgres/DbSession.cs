using DocRag.Ingestion.Application.Abstractions;
using Npgsql;

namespace DocRag.Ingestion.Infrastructure.Postgres;

/// <summary>
/// Scoped connection/transaction holder shared by every repository of one request or unit of
/// work. Repositories enlist in the open transaction automatically; committing or rolling back
/// returns the connection to the pool, so long-running work (such as streaming an upload) never
/// holds one.
/// </summary>
public sealed class DbSession(NpgsqlDataSource dataSource) : IUnitOfWork, IDisposable
{
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;

    /// <summary>Gets the open transaction, or <see langword="null"/> outside one.</summary>
    public NpgsqlTransaction? Transaction => _transaction;

    /// <summary>Gets the scope's connection, opening it on first use.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An open connection.</returns>
    public async ValueTask<NpgsqlConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        _connection ??= await dataSource.OpenConnectionAsync(cancellationToken);
        return _connection;
    }

    /// <inheritdoc />
    public async Task BeginAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        _transaction ??= await connection.BeginTransactionAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_transaction is not null)
            {
                await _transaction.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            await ReleaseAsync();
        }
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_transaction is not null)
            {
                await _transaction.RollbackAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or ObjectDisposedException)
        {
            // The connection is already broken: nothing is left to roll back, and it is discarded below.
        }
        finally
        {
            await ReleaseAsync();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await RollbackAsync();

    /// <summary>
    /// Synchronous disposal for containers that cannot await (e.g. the warm-up preloader's scope);
    /// disposing the connection rolls back any open transaction.
    /// </summary>
    public void Dispose()
    {
        _transaction?.Dispose();
        _transaction = null;
        _connection?.Dispose();
        _connection = null;
    }

    private async Task ReleaseAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
