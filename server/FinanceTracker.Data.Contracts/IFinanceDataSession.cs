using System.Data;

namespace FinanceTracker.Data.Contracts;

public interface IFinanceDataSession
{
    Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        object? parameters = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<T?> QuerySingleOrDefaultAsync<T>(
        string sql,
        object? parameters = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<T> QuerySingleAsync<T>(
        string sql,
        object? parameters = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<int> ExecuteAsync(
        string sql,
        object? parameters = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<T> SaveAsync<T>(
        T entity,
        string auditUserId,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
        where T : class;

    Task ExecuteInTransactionAsync(
        Func<IDbConnection, IDbTransaction, Task> work,
        CancellationToken cancellationToken = default);

    Task<T> ExecuteInTransactionAsync<T>(
        Func<IDbConnection, IDbTransaction, Task<T>> work,
        CancellationToken cancellationToken = default);
}

