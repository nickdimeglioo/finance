using System.Collections.Concurrent;
using System.Data;
using Npgsql;

namespace FinanceTracker.Api.Infrastructure.Data.SqlMapper;

public interface IConnectionProvider
{
    IDbConnection GetConnection();
    IDbConnection GetUsersConnection();
}

public sealed class FinanceConnectionProvider : IConnectionProvider
{
    private static readonly ConcurrentDictionary<string, NpgsqlDataSource> DataSources = new();
    private readonly string _connectionString;

    public FinanceConnectionProvider(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public IDbConnection GetConnection()
    {
        var builder = new NpgsqlConnectionStringBuilder(_connectionString)
        {
            ApplicationName = "FinanceTracker.Api",
            Timezone = "UTC"
        };

        return GetDataSource(builder.ConnectionString).CreateConnection();
    }

    public IDbConnection GetUsersConnection()
    {
        return GetConnection();
    }

    private static NpgsqlDataSource GetDataSource(string connectionString)
    {
        return DataSources.GetOrAdd(connectionString, static value =>
        {
            var builder = new NpgsqlDataSourceBuilder(value);
            return builder.Build();
        });
    }
}

public interface IOrmMapperService
{
    Task<IEnumerable<T>> ExecuteRawQueryAsync<T>(
        string sql,
        object? parameters = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null);

    Task<T?> ExecuteRawScalarAsync<T>(
        string sql,
        object? parameters = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null);

    Task<int> ExecuteAsync(
        string sql,
        object? parameters = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null);

    Task<T> QuerySingleAsync<T>(
        string sql,
        object? parameters = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null);

    Task<T> SaveAsync<T>(
        T entity,
        SaveQuery? saveQuery = null,
        bool useTransaction = true,
        string auditUserId = "System",
        IDbConnection? connection = null,
        IDbTransaction? transaction = null)
        where T : class;

    IDbConnection CreateConnection();
}

public sealed class FinanceOrmMapperService : IOrmMapperService
{
    private readonly IConnectionProvider _connectionProvider;
    private readonly IReadOnlyList<IDbInterceptor> _interceptors;

    public FinanceOrmMapperService(IConnectionProvider connectionProvider, IServiceProvider services)
    {
        _connectionProvider = connectionProvider;
        _interceptors = services.GetServices<IDbInterceptor>().ToList();
    }

    public async Task<IEnumerable<T>> ExecuteRawQueryAsync<T>(
        string sql,
        object? parameters = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null)
    {
        return await WithConnectionAsync(
            activeConnection => OrmMapper.ExecuteRawQuery<T>(sql, parameters, activeConnection, transaction),
            connection,
            transaction);
    }

    public async Task<T?> ExecuteRawScalarAsync<T>(
        string sql,
        object? parameters = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null)
    {
        return await WithConnectionAsync(
            activeConnection => OrmMapper.ExecuteRawScalar<T>(sql, parameters, activeConnection, transaction),
            connection,
            transaction);
    }

    public async Task<int> ExecuteAsync(
        string sql,
        object? parameters = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null)
    {
        return await WithConnectionAsync(
            activeConnection => OrmMapper.Execute(sql, parameters, activeConnection, transaction),
            connection,
            transaction);
    }

    public async Task<T> QuerySingleAsync<T>(
        string sql,
        object? parameters = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null)
    {
        return await WithConnectionAsync(
            activeConnection => OrmMapper.QuerySingle<T>(sql, parameters, activeConnection, transaction),
            connection,
            transaction);
    }

    public async Task<T> SaveAsync<T>(
        T entity,
        SaveQuery? saveQuery = null,
        bool useTransaction = true,
        string auditUserId = "System",
        IDbConnection? connection = null,
        IDbTransaction? transaction = null)
        where T : class
    {
        return await WithConnectionAsync(
            activeConnection => OrmMapper.SaveAsync(
                entity,
                saveQuery,
                useTransaction,
                auditUserId,
                activeConnection,
                transaction,
                _interceptors),
            connection,
            transaction);
    }

    public IDbConnection CreateConnection()
    {
        return _connectionProvider.GetUsersConnection();
    }

    private async Task<T> WithConnectionAsync<T>(
        Func<IDbConnection, Task<T>> action,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null)
    {
        var activeConnection = connection ?? transaction?.Connection ?? _connectionProvider.GetUsersConnection();
        var ownsConnection = connection is null && transaction?.Connection is null;

        if (activeConnection.State != ConnectionState.Open)
        {
            activeConnection.Open();
        }

        try
        {
            return await action(activeConnection);
        }
        finally
        {
            if (ownsConnection)
            {
                activeConnection.Dispose();
            }
        }
    }
}
