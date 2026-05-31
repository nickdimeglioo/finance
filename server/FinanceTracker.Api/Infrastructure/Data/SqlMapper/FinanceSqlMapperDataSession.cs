using System.Data;
using FinanceTracker.Api.Infrastructure.Data;
using FinanceTracker.Data.Contracts;

namespace FinanceTracker.Api.Infrastructure.Data.SqlMapper;

public sealed class FinanceSqlMapperDataSession : IFinanceDataSession
{
    private readonly NpgsqlConnectionFactory _connectionFactory;
    private readonly IReadOnlyList<IDbInterceptor> _interceptors;

    public FinanceSqlMapperDataSession(NpgsqlConnectionFactory connectionFactory, IServiceProvider services)
    {
        _connectionFactory = connectionFactory;
        _interceptors = services.GetServices<IDbInterceptor>().ToList();
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        object? parameters = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        if (connection is not null)
        {
            return (await OrmMapper.ExecuteRawQuery<T>(sql, parameters, connection, transaction)).ToList();
        }

        await using var owned = _connectionFactory.CreateConnection();
        await owned.OpenAsync(cancellationToken);
        return (await OrmMapper.ExecuteRawQuery<T>(sql, parameters, owned)).ToList();
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        string sql,
        object? parameters = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        if (connection is not null)
        {
            return (await OrmMapper.ExecuteRawQuery<T>(sql, parameters, connection, transaction)).SingleOrDefault();
        }

        await using var owned = _connectionFactory.CreateConnection();
        await owned.OpenAsync(cancellationToken);
        return (await OrmMapper.ExecuteRawQuery<T>(sql, parameters, owned)).SingleOrDefault();
    }

    public async Task<T> QuerySingleAsync<T>(
        string sql,
        object? parameters = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        if (connection is not null)
        {
            return await OrmMapper.QuerySingle<T>(sql, parameters, connection, transaction);
        }

        await using var owned = _connectionFactory.CreateConnection();
        await owned.OpenAsync(cancellationToken);
        return await OrmMapper.QuerySingle<T>(sql, parameters, owned);
    }

    public async Task<int> ExecuteAsync(
        string sql,
        object? parameters = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        if (connection is not null)
        {
            return await OrmMapper.Execute(sql, parameters, connection, transaction);
        }

        await using var owned = _connectionFactory.CreateConnection();
        await owned.OpenAsync(cancellationToken);
        return await OrmMapper.Execute(sql, parameters, owned);
    }

    public async Task<T> SaveAsync<T>(
        T entity,
        string auditUserId,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        if (connection is not null)
        {
            return await OrmMapper.SaveAsync(entity, useTransaction: transaction is null, auditUserId: auditUserId, conn: connection, trans: transaction, interceptors: _interceptors);
        }

        await using var owned = _connectionFactory.CreateConnection();
        await owned.OpenAsync(cancellationToken);
        return await OrmMapper.SaveAsync(entity, useTransaction: true, auditUserId: auditUserId, conn: owned, interceptors: _interceptors);
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
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var result = await work(connection, transaction);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
