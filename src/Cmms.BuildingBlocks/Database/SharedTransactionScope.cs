using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cmms.BuildingBlocks.Database;

/// <summary>
/// Cross-module transaction primitive, per docs/03-architecture-decisions.md's "Solution layout"
/// note: "anything that must atomically touch two modules' data ... shares one explicit
/// transaction rather than reaching for a message broker to avoid a local transaction."
///
/// Each module owns its own <see cref="DbContext"/> (schema-per-module), so there is no single
/// shared DbContext to hang a transaction off of. This type opens one physical ADO.NET connection
/// + transaction and lets each caller mint short-lived DbContext instances against that same
/// connection, so their combined SaveChanges calls commit or roll back together — e.g. an Assets
/// module mutation and the Audit module's event row for it (docs/02-security-and-invariants.md
/// § "Audit trail": "Written in the same transaction as the domain change, by the domain command
/// itself").
/// </summary>
public sealed class SharedTransactionScope : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private bool _committed;

    private SharedTransactionScope(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public static async Task<SharedTransactionScope> BeginAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var transaction = await connection.BeginTransactionAsync(cancellationToken);
        return new SharedTransactionScope(connection, transaction);
    }

    /// <summary>
    /// Creates a DbContext enlisted in this scope's shared connection/transaction. The caller is
    /// responsible for disposing the returned context (it does not own the connection).
    /// </summary>
    public TContext CreateContext<TContext>(Func<DbContextOptions<TContext>, TContext> factory)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;

        var context = factory(options);
        context.Database.UseTransaction(_transaction);
        return context;
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _transaction.CommitAsync(cancellationToken);
        _committed = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _transaction.RollbackAsync(cancellationToken);
        _committed = true; // prevent an implicit rollback attempt in DisposeAsync after an explicit one
    }

    public async ValueTask DisposeAsync()
    {
        if (!_committed)
        {
            try
            {
                await _transaction.RollbackAsync();
            }
            catch
            {
                // connection may already be broken/closed; nothing more to do.
            }
        }

        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
