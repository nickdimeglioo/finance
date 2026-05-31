using System.Data;
using FinanceTracker.Data.Contracts;

namespace FinanceTracker.Api.Infrastructure.Data.SqlMapper;

public sealed class FinanceSqlMapperDataSession : IFinanceDataSession
{
    private readonly IOrmMapperService _mapper;

    public FinanceSqlMapperDataSession(IOrmMapperService mapper)
    {
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        object? parameters = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        return (await _mapper.ExecuteRawQueryAsync<T>(sql, parameters, connection, transaction)).ToList();
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        string sql,
        object? parameters = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        return (await _mapper.ExecuteRawQueryAsync<T>(sql, parameters, connection, transaction)).SingleOrDefault();
    }

    public async Task<T> QuerySingleAsync<T>(
        string sql,
        object? parameters = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        return await _mapper.QuerySingleAsync<T>(sql, parameters, connection, transaction);
    }

    public async Task<int> ExecuteAsync(
        string sql,
        object? parameters = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        return await _mapper.ExecuteAsync(sql, parameters, connection, transaction);
    }

    public async Task<T> SaveAsync<T>(
        T entity,
        string auditUserId,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        return await _mapper.SaveAsync(entity, useTransaction: transaction is null, auditUserId: auditUserId, connection: connection, transaction: transaction);
    }

    public async Task ExecuteInTransactionAsync(Func<IDbConnection, IDbTransaction, Task> work, CancellationToken cancellationToken = default)
    {
        await ExecuteInTransactionAsync<object?>(async (connection, transaction) =>
        {
            await work(connection, transaction);
            return null;
        }, cancellationToken);
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<IDbConnection, IDbTransaction, Task<T>> work, CancellationToken cancellationToken = default)
    {
        using var connection = _mapper.CreateConnection();
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        using var transaction = connection.BeginTransaction();

        try
        {
            var result = await work(connection, transaction);
            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
