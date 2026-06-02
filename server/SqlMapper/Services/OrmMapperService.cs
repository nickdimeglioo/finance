using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PipelineRunner.Utils;

namespace PipelineRunner.Services
{
    /// <summary>
    /// Service interface for ORM operations - register this in DI container
    /// </summary>
    public interface IOrmMapperService
    {
        // Get operations
        Task<T?> GetByIdAsync<T>(object? id, int depth = -1, HashSet<Type>? ignoredTypes = null, SelectQuery? selectQuery = null, bool ignoreResourcePermissions = false) where T : class, new();
        Task<IEnumerable<T>> GetAllAsync<T>(IEnumerable<object>? ids = null, int depth = -1, HashSet<Type>? ignoredTypes = null, int batchSize = 1000, SelectQuery? selectQuery = null, bool ignoreResourcePermissions = false) where T : class, new();
        OrmQueryable<T> Query<T>(SelectQuery? selectQuery = null, bool ignoreResourcePermissions = false) where T : class, new();
        OrmSelectQueryable<T> QuerySelect<T>(SelectQuery? selectQuery = null, bool ignoreResourcePermissions = false) where T : class, new();
        
        // Save operations
        Task<T> SaveAsync<T>(T entity, SaveQuery? saveQuery = null, bool useTransaction = true, string auditUserId = "System", IDbConnection? conn = null, IDbTransaction? trans = null, bool ignoreResourcePermissions = false) where T : class;
        Task<IEnumerable<T>> SaveBulkAsync<T>(IEnumerable<T> entities, BulkSaveOptions? options = null, bool ignoreResourcePermissions = false) where T : class;
        
        // Delete operations
        Task<bool> DeleteAsync<T>(T entity, DeleteType deleteType = DeleteType.Full, DeleteQuery? deleteQuery = null, IDbConnection? conn = null, IDbTransaction? trans = null, bool useTransaction = true, string userId = "System", bool ignoreResourcePermissions = false) where T : class, new();
        Task<bool> DeleteByIdAsync<T>(object id, DeleteType deleteType = DeleteType.Full, DeleteQuery? deleteQuery = null, IDbConnection? conn = null, IDbTransaction? trans = null, bool useTransaction = true, string userId = "System", bool ignoreResourcePermissions = false) where T : class, new();
        Task<int> DeleteBulkAsync<T>(IEnumerable<T> entities, BulkDeleteOptions? options = null, bool ignoreResourcePermissions = false) where T : class, new();
        
        // Raw query operations
        Task<IEnumerable<T>> ExecuteRawQueryAsync<T>(string sql, object? parameters = null, IDbConnection? conn = null, IDbTransaction? tran = null);
        Task<T?> ExecuteRawScalarAsync<T>(string sql, object? parameters = null, IDbConnection? conn = null, IDbTransaction? tran = null);
        Task<int> ExecuteAsync<T>(string sql, object? parameters = null, IDbConnection? conn = null, IDbTransaction? tran = null);
        Task<T> QuerySingleAsync<T>(string sql, object? parameters = null, IDbConnection? conn = null, IDbTransaction? tran = null);

        // Transaction operations
        MultiTransaction StartMultiTransaction(IsolationLevel? isolationLevel = null, IDbConnection? connection = null, bool ignoreResourcePermissions = false);
        MultiTransaction BeginMultiTransaction(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, IDbConnection? connection = null, bool ignoreResourcePermissions = false);
    }

    /// <summary>
    /// Default implementation of ORM service - thin wrapper around static OrmMapper
    /// </summary>
    public class OrmMapperService : IOrmMapperService
    {
        private readonly IConnectionProvider _connectionProvider;
        private readonly IExecutionContext? _executionContext;
        private readonly IReadOnlyList<IDbInterceptor> _interceptors;
        private readonly OrmMapperOptions _options;

        public OrmMapperService(
            IConnectionProvider connectionProvider,
            IServiceProvider? serviceProvider = null,
            IExecutionContext? executionContext = null,
            OrmMapperOptions? options = null)
        {
            _connectionProvider = connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));
            _executionContext = executionContext;
            _interceptors = serviceProvider?.GetServices<IDbInterceptor>().ToList() ?? [];
            if (options != null)
            {
                OrmMapper.Configure(options);
            }

            _options = OrmMapper.Options;
        }

        private async Task<TResult> WithUsersConnectionAsync<TResult>(
            Func<IDbConnection, Task<TResult>> action,
            IDbConnection? conn = null,
            IDbTransaction? tran = null)
        {
            var connection = conn ?? tran?.Connection ?? _connectionProvider.GetUsersConnection();
            var ownsConnection = conn == null && tran?.Connection == null;

            if (ownsConnection && connection.State != ConnectionState.Open)
                connection.Open();

            try
            {
                return await action(connection);
            }
            finally
            {
                if (ownsConnection)
                    connection.Dispose();
            }
        }

        #region Get Operations

        public async Task<T?> GetByIdAsync<T>(object? id, int depth = -1, HashSet<Type>? ignoredTypes = null, SelectQuery? selectQuery = null, bool ignoreResourcePermissions = false) where T : class, new()
        {
            if (selectQuery?.CurrentConnection != null)
            {
                ApplyInterceptors(selectQuery);
                return await OrmMapper.GetByIdAsync<T>(id, depth, ignoredTypes, selectQuery);
            }

            return await WithUsersConnectionAsync(async connection =>
            {
                var sq = selectQuery ?? new SelectQuery();
                sq.CurrentConnection = connection;
                ApplyInterceptors(sq);
                return await OrmMapper.GetByIdAsync<T>(id, depth, ignoredTypes, sq);
            });
        }

        public async Task<IEnumerable<T>> GetAllAsync<T>(IEnumerable<object>? ids = null, int depth = -1, HashSet<Type>? ignoredTypes = null, int batchSize = 1000, SelectQuery? selectQuery = null, bool ignoreResourcePermissions = false) where T : class, new()
        {
            if (selectQuery?.CurrentConnection != null)
            {
                ApplyInterceptors(selectQuery);
                return await OrmMapper.GetAllAsync<T>(ids, depth, ignoredTypes, batchSize, selectQuery);
            }

            return await WithUsersConnectionAsync(async connection =>
            {
                var sq = selectQuery ?? new SelectQuery();
                sq.CurrentConnection = connection;
                ApplyInterceptors(sq);
                return await OrmMapper.GetAllAsync<T>(ids, depth, ignoredTypes, batchSize, sq);
            });
        }

        public OrmQueryable<T> Query<T>(SelectQuery? selectQuery = null, bool ignoreResourcePermissions = false) where T : class, new()
        {
            var sq = selectQuery ?? new SelectQuery();
            if (sq.CurrentConnection == null)
            {
                sq.CurrentConnection = sq.CurrentTransaction?.Connection ?? _connectionProvider.GetUsersConnection();
                sq.ManageConnectionLifecycle = sq.CurrentTransaction == null;
            }
            ApplyInterceptors(sq);

            return OrmMapper.QueryGet<T>(sq);
        }

        public OrmSelectQueryable<T> QuerySelect<T>(SelectQuery? selectQuery = null, bool ignoreResourcePermissions = false) where T : class, new()
        {
            var sq = selectQuery ?? new SelectQuery();
            if (sq.CurrentConnection == null)
            {
                sq.CurrentConnection = sq.CurrentTransaction?.Connection ?? _connectionProvider.GetUsersConnection();
                sq.ManageConnectionLifecycle = sq.CurrentTransaction == null;
            }
            ApplyInterceptors(sq);

            return OrmMapper.QuerySelect<T>(sq);
        }

        #endregion

        #region Save Operations

        public async Task<T> SaveAsync<T>(T entity, SaveQuery? saveQuery = null, bool useTransaction = true, string auditUserId = "System", IDbConnection? conn = null, IDbTransaction? trans = null, bool ignoreResourcePermissions = false) where T : class
        {
            auditUserId = ResolveAuditUserId(auditUserId);

            if (conn != null || trans?.Connection != null)
            {
                var connection = conn ?? trans?.Connection;
                return await OrmMapper.SaveAsync(entity, saveQuery, useTransaction, auditUserId, connection!, trans, _interceptors);
            }

            return await WithUsersConnectionAsync(
                connection => OrmMapper.SaveAsync(entity, saveQuery, useTransaction, auditUserId, connection, trans, _interceptors));
        }

        public async Task<IEnumerable<T>> SaveBulkAsync<T>(IEnumerable<T> entities, BulkSaveOptions? options = null, bool ignoreResourcePermissions = false) where T : class
        {
            var entityList = entities as IList<T> ?? entities.ToList();
            options ??= new BulkSaveOptions();
            options.AuditUserId = ResolveAuditUserId(options.AuditUserId);
            ApplyInterceptors(options);

            if (options.CurrentConnection != null || options.Transaction?.Connection != null)
            {
                if (options.CurrentConnection == null)
                    options.CurrentConnection = options.Transaction!.Connection;

                return await OrmMapper.SaveBulkAsync(entityList, options);
            }

            return await WithUsersConnectionAsync(async connection =>
            {
                options.CurrentConnection = connection;
                return await OrmMapper.SaveBulkAsync(entityList, options);
            });
        }

        #endregion

        #region Delete Operations

        public async Task<bool> DeleteAsync<T>(T entity, DeleteType deleteType = DeleteType.Full, DeleteQuery? deleteQuery = null, IDbConnection? conn = null, IDbTransaction? trans = null, bool useTransaction = true, string userId = "System", bool ignoreResourcePermissions = false) where T : class, new()
        {
            userId = ResolveAuditUserId(userId);

            if (conn != null || trans?.Connection != null)
            {
                var connection = conn ?? trans?.Connection;
                return await OrmMapper.DeleteAsync(entity, deleteType, deleteQuery, connection!, trans, useTransaction, userId, _interceptors);
            }

            return await WithUsersConnectionAsync(
                connection => OrmMapper.DeleteAsync(entity, deleteType, deleteQuery, connection, trans, useTransaction, userId, _interceptors));
        }

        public async Task<bool> DeleteByIdAsync<T>(object id, DeleteType deleteType = DeleteType.Full, DeleteQuery? deleteQuery = null, IDbConnection? conn = null, IDbTransaction? trans = null, bool useTransaction = true, string userId = "System", bool ignoreResourcePermissions = false) where T : class, new()
        {
            userId = ResolveAuditUserId(userId);

            if (conn != null || trans?.Connection != null)
            {
                var connection = conn ?? trans?.Connection;
                return await OrmMapper.DeleteByIdAsync<T>(id, deleteType, deleteQuery, connection!, trans, useTransaction, userId, _interceptors);
            }

            return await WithUsersConnectionAsync(
                connection => OrmMapper.DeleteByIdAsync<T>(id, deleteType, deleteQuery, connection, trans, useTransaction, userId, _interceptors));
        }

        public async Task<int> DeleteBulkAsync<T>(IEnumerable<T> entities, BulkDeleteOptions? options = null, bool ignoreResourcePermissions = false) where T : class, new()
        {
            var entityList = entities as IList<T> ?? entities.ToList();
            options ??= new BulkDeleteOptions();
            options.UserId = ResolveAuditUserId(options.UserId);
            ApplyInterceptors(options);

            if (options.CurrentConnection != null || options.Transaction?.Connection != null)
            {
                if (options.CurrentConnection == null)
                    options.CurrentConnection = options.Transaction!.Connection;

                return await OrmMapper.DeleteBulkAsync(entityList, options);
            }

            return await WithUsersConnectionAsync(async connection =>
            {
                options.CurrentConnection = connection;
                return await OrmMapper.DeleteBulkAsync(entityList, options);
            });
        }

        #endregion

        #region Raw Query Operations

        public async Task<IEnumerable<T>> ExecuteRawQueryAsync<T>(string sql, object? parameters = null, IDbConnection? conn = null, IDbTransaction? tran = null)
        {
            if (conn != null || tran?.Connection != null)
            {
                var connection = conn ?? tran?.Connection;
                return await OrmMapper.ExecuteRawQuery<T>(sql, parameters, connection!, tran);
            }

            return await WithUsersConnectionAsync(
                connection => OrmMapper.ExecuteRawQuery<T>(sql, parameters, connection, tran));
        }

        public async Task<T?> ExecuteRawScalarAsync<T>(string sql, object? parameters = null, IDbConnection? conn = null, IDbTransaction? tran = null)
        {
            if (conn != null || tran?.Connection != null)
            {
                var connection = conn ?? tran?.Connection;
                return await OrmMapper.ExecuteRawScalar<T>(sql, parameters, connection!, tran);
            }

            return await WithUsersConnectionAsync(
                connection => OrmMapper.ExecuteRawScalar<T>(sql, parameters, connection, tran));
        }

        public async Task<int> ExecuteAsync<T>(string sql, object? parameters = null, IDbConnection? conn = null, IDbTransaction? tran = null)
        {
            if (conn != null || tran?.Connection != null)
            {
                var connection = conn ?? tran?.Connection;
                return await OrmMapper.Execute<T>(sql, parameters, connection!, tran);
            }

            return await WithUsersConnectionAsync(
                connection => OrmMapper.Execute<T>(sql, parameters, connection, tran));
        }

        public async Task<T> QuerySingleAsync<T>(string sql, object? parameters = null, IDbConnection? conn = null, IDbTransaction? tran = null)
        {
            if (conn != null || tran?.Connection != null)
            {
                var connection = conn ?? tran?.Connection;
                return await OrmMapper.QuerySingle<T>(sql, parameters, connection!, tran);
            }

            return await WithUsersConnectionAsync(
                connection => OrmMapper.QuerySingle<T>(sql, parameters, connection, tran));
        }

        #endregion

        #region Transaction Operations

        public MultiTransaction BeginMultiTransaction(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, IDbConnection? connection = null, bool ignoreResourcePermissions = false)
        {
            return StartMultiTransaction(isolationLevel, connection, ignoreResourcePermissions);
        }

        public MultiTransaction StartMultiTransaction(IsolationLevel? isolationLevel = null, IDbConnection? connection = null, bool ignoreResourcePermissions = false)
        {
            var conn = connection ?? _connectionProvider.GetUsersConnection();
            return new MultiTransaction(
                conn,
                isolationLevel ?? _options.DefaultTransactionIsolationLevel,
                ResolveAuditUserId(),
                _interceptors);
        }

        #endregion

        private string ResolveAuditUserId(string? requestedAuditUserId = null)
        {
            if (_executionContext?.UserId is Guid userId && userId != Guid.Empty)
                return userId.ToString("D");

            return string.IsNullOrWhiteSpace(requestedAuditUserId)
                ? "System"
                : requestedAuditUserId;
        }

        private void ApplyInterceptors(SelectQuery selectQuery)
        {
            selectQuery.Interceptors ??= _interceptors;
        }

        private void ApplyInterceptors(BulkSaveOptions options)
        {
            options.Interceptors ??= _interceptors;
        }

        private void ApplyInterceptors(BulkDeleteOptions options)
        {
            options.Interceptors ??= _interceptors;
        }
    }

    /// <summary>
    /// Interface for providing database connections - implement this based on your setup
    /// </summary>
    public interface IConnectionProvider
    {
        IDbConnection GetConnection();
        IDbConnection GetUsersConnection();
    }

    /// <summary>
    /// Example implementation of connection provider
    /// Replace with your actual connection management logic
    /// </summary>
    public class DefaultConnectionProvider : IConnectionProvider
    {
        private static readonly ConcurrentDictionary<string, NpgsqlDataSource> _dataSourceCache = new();
        private readonly string _connectionString;
        private readonly IExecutionContext? _executionContext;

        public DefaultConnectionProvider(string connectionString, IExecutionContext? executionContext = null)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _executionContext = executionContext;
        }

        public IDbConnection GetConnection()
        {
            // Replace with your actual connection creation logic
            // Example for Npgsql:
            var org = _executionContext?.OrganizationId;
            var proj = _executionContext?.ProjectId;
            var user = _executionContext?.UserId;
            var csb = new NpgsqlConnectionStringBuilder(_connectionString)
            {
                ApplicationName = "MyApp",
                Timezone = "UTC",
                Options = $"-c app.organization_id={org} -c app.project_id={proj} -c app.user_id={user}"
            };

            return GetDataSource(csb.ConnectionString).CreateConnection();
        }

        public IDbConnection GetUsersConnection()
        {
            // If you have a separate users database, provide that connection here
            // Otherwise return the same as GetConnection()
            return GetConnection();
        }

        private static NpgsqlDataSource GetDataSource(string connectionString)
        {
            return _dataSourceCache.GetOrAdd(connectionString, cs =>
            {
                var builder = new NpgsqlDataSourceBuilder(cs);
                return builder.Build();
            });
        }
    }
}
