using System;
using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Dapper;
using PipelineRunner.Services;

namespace PipelineRunner.Utils
{

    public class MultiTransactionQueryable<T> where T : class, new()
    {
        private OrmQueryable<T> _query;
        private IDbTransaction _transaction;
        private IDbConnection _connection;
        private bool _lockRow;
        private bool _noWait;


        public MultiTransactionQueryable(IDbConnection connection, IDbTransaction transaction, bool lockRow = false, bool noWait = false)
        {
            _query = new OrmQueryable<T>();
            _transaction = transaction;
            _connection = connection;
            _lockRow = lockRow;
            _noWait = noWait;
        }
        public MultiTransactionQueryable<T> Where(Expression<Func<T, bool>> predicate)
        {
            _query = _query.Where(predicate);
            return this;
        }

        public async Task<List<T>> ToListAsync()
        {
            SelectQuery sq = new SelectQuery()
            {
                CurrentConnection = _connection,
                CurrentTransaction = _transaction,
                LockRow = _lockRow,
                NoWait = _noWait
            };
            return await _query.ToListAsync(selectQuery: sq);
        }

        public async Task<T?> FirstOrDefaultAsync()
        {
            SelectQuery sq = new SelectQuery()
            {
                CurrentConnection = _connection,
                CurrentTransaction = _transaction,
                LockRow = _lockRow,
                NoWait = _noWait
            };
            return await _query.FirstOrDefaultAsync(selectQuery: sq);
        }
    }

    public class MultiTransaction : IDisposable, IAsyncDisposable
    {
        private BulkSaveOptions _bulkSaveOptions;
        private BulkDeleteOptions _bulkDeleteOptions;
        private IDbConnection _connection;
        private IDbTransaction _transaction;
        private bool _connOpened = false;
        private bool _inTransaction = false;
        private bool _error = false;
        private bool _committed = false;
        private bool _disposed = false;
        private readonly string _auditUserId;
        private readonly IReadOnlyList<IDbInterceptor>? _interceptors;

        public MultiTransaction() : this(null)
        {
        }

        public MultiTransaction(
            IDbConnection connection,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
            string auditUserId = "System",
            IReadOnlyList<IDbInterceptor>? interceptors = null)
        {
            _connection = connection ?? DbConnection.UsersConnection;
            IsolationLevel = isolationLevel;
            _auditUserId = string.IsNullOrWhiteSpace(auditUserId) ? "System" : auditUserId;
            _interceptors = interceptors;
        }

        public IsolationLevel IsolationLevel { get; }
        public bool HasError => _error;
        public bool IsCommitted => _committed;
        public bool IsInTransaction => _inTransaction;
        public IDbTransaction Transaction => _transaction;
        public IDbConnection Connection => _connection;

        #region Save Operations
        public async Task Save<T>(T entityToSave) where T : class
        {
            ThrowIfDisposed();
            Open();
            try
            {
                await OrmMapper.SaveAsync(entityToSave, conn: _connection, trans: _transaction, useTransaction: false, auditUserId: _auditUserId, interceptors: _interceptors);
            }
            catch (Exception ex)
            {
                await HandleOperationExceptionAsync(ex, "Save", typeof(T));
                throw;
            }
        }

        public async Task SaveBulk<T>(IEnumerable<T> entitiesToSave) where T : class
        {
            ThrowIfDisposed();
            InitializeBulkOptions();
            Open();
            try
            {
                _bulkSaveOptions.Interceptors ??= _interceptors;
                await OrmMapper.SaveBulkAsync<T>(entitiesToSave, _bulkSaveOptions);
            }
            catch (Exception ex)
            {
                await HandleOperationExceptionAsync(ex, "SaveBulk", typeof(T));
                throw;
            }
        }
        #endregion

        #region Delete Operations
        public async Task Delete<T>(T entityToDelete) where T : class, new()
        {
            ThrowIfDisposed();
            Open();
            try
            {
                await OrmMapper.DeleteAsync<T>(entityToDelete, conn: _connection, trans: _transaction, useTransaction: false, userId: _auditUserId, interceptors: _interceptors);
            }
            catch (Exception ex)
            {
                await HandleOperationExceptionAsync(ex, "Delete", typeof(T));
                throw;
            }
        }

        public async Task DeleteById<T>(object id) where T : class, new()
        {
            ThrowIfDisposed();
            Open();
            try
            {
                await OrmMapper.DeleteByIdAsync<T>(id, conn: _connection, trans: _transaction, useTransaction: true, userId: _auditUserId, interceptors: _interceptors);
            }
            catch (Exception ex)
            {
                await HandleOperationExceptionAsync(ex, "DeleteById", typeof(T));
                throw;
            }
        }

        public async Task DeleteBulk<T>(IEnumerable<T> entitiesToDelete) where T : class, new()
        {
            ThrowIfDisposed();
            InitializeBulkOptions();
            Open();
            try
            {
                _bulkDeleteOptions.Interceptors ??= _interceptors;
                await OrmMapper.DeleteBulkAsync<T>(entitiesToDelete, _bulkDeleteOptions);
            }
            catch (Exception ex)
            {
                await HandleOperationExceptionAsync(ex, "DeleteBulk", typeof(T));
                throw;
            }
        }
        #endregion
        #region Get Operations
        //public async Task GetByIdAsync<T>(T entityToSave) where T : class
        //{
        //    ThrowIfDisposed();
        //    Open();
        //    try
        //    {
        //        await OrmMapper.SaveAsync(entityToSave, conn: _connection, trans: _transaction, useTransaction: false);
        //    }
        //    catch (Exception)
        //    {
        //        _error = true;
        //        await RollbackAsync();
        //        throw;
        //    }
        //}
        public async Task<T?> GetByIdAsync<T>(object? id, bool lockRow = false, bool noWait = false) where T : class, new()
        {
            ThrowIfDisposed();
            Open();
            try
            {
                SelectQuery sq = new SelectQuery()
                {
                    CurrentTransaction = _transaction,
                    CurrentConnection = _connection,
                    UseTransaction = false,
                    LockRow = lockRow,
                    NoWait = noWait,
                    Interceptors = _interceptors
                };
                return await OrmMapper.GetByIdAsync<T>(id, selectQuery: sq);
            }
            catch (Exception ex)
            {
                await HandleOperationExceptionAsync(ex, "GetById", typeof(T));
                throw;
            }
        }

        public async Task<IEnumerable<T>> GetAllAsync<T>(IEnumerable<object>? ids, bool lockRow = false, bool noWait = false) where T : class, new()
        {
            ThrowIfDisposed();
            Open();
            try
            {
                SelectQuery sq = new SelectQuery()
                {
                    CurrentTransaction = _transaction,
                    CurrentConnection = _connection,
                    UseTransaction = false,
                    LockRow = lockRow,
                    NoWait = noWait,
                    Interceptors = _interceptors
                };
                return await OrmMapper.GetAllAsync<T>(ids, selectQuery: sq);
            }
            catch (Exception ex)
            {
                await HandleOperationExceptionAsync(ex, "GetAll", typeof(T));
                throw;
            }
        }

        public OrmQueryable<T> QueryGet<T>(bool lockRow = false, bool noWait = false) where T : class, new()
        {
            ThrowIfDisposed();
            Open();
            SelectQuery sq = new SelectQuery()
            {
                CurrentTransaction = _transaction,
                CurrentConnection = _connection,
                UseTransaction = false,
                LockRow = lockRow,
                NoWait = noWait,
                Interceptors = _interceptors
            };
            return OrmMapper.QueryGet<T>(sq);
        }

        public OrmSelectQueryable<T> QuerySelect<T>(bool lockRow = false, bool noWait = false) where T : class, new()
        {
            ThrowIfDisposed();
            Open();
            SelectQuery sq = new SelectQuery()
            {
                CurrentTransaction = _transaction,
                CurrentConnection = _connection,
                UseTransaction = false,
                LockRow = lockRow,
                NoWait = noWait,
                Interceptors = _interceptors
            };
            return OrmMapper.QuerySelect<T>(sq);
        }

        public async Task<int> Execute(string query, object? parameters = null)
        {
            ThrowIfDisposed();
            Open();
            try
            {
                SelectQuery sq = new SelectQuery()
                {
                    CurrentTransaction = _transaction,
                    CurrentConnection = _connection,
                    UseTransaction = false,
                };
                return await OrmMapper.Execute(query, parameters, _connection, _transaction);
            }
            catch (Exception ex)
            {
                await HandleOperationExceptionAsync(ex, "ExecuteRaw");
                throw;
            }
        }


        public async Task<int> Execute<T>(string query, object? parameters = null)
        {
            ThrowIfDisposed();
            Open();
            try
            {
                SelectQuery sq = new SelectQuery()
                {
                    CurrentTransaction = _transaction,
                    CurrentConnection = _connection,
                    UseTransaction = false,
                };
                return await OrmMapper.Execute<T>(query, parameters, _connection, _transaction);
            }
            catch (Exception ex)
            {
                await HandleOperationExceptionAsync(ex, "ExecuteTyped", typeof(T));
                throw;
            }
        }

        public async Task<IEnumerable<T>> ExecuteRawQuery<T>(string query, object? parameters = null)
        {
            ThrowIfDisposed();
            Open();
            try
            {
                SelectQuery sq = new SelectQuery()
                {
                    CurrentTransaction = _transaction,
                    CurrentConnection = _connection,
                    UseTransaction = false,
                };
                return await OrmMapper.ExecuteRawQuery<T>(query, parameters, _connection, _transaction);
            }
            catch (Exception ex)
            {
                await HandleOperationExceptionAsync(ex, "ExecuteRawQuery", typeof(T));
                throw;
            }
        }
        public async Task<T?> ExecuteRawScalar<T>(string query, object? parameters = null)
        {
            ThrowIfDisposed();
            Open();
            try
            {
                return await OrmMapper.ExecuteRawScalar<T>(query, parameters, _connection, _transaction);
            }
            catch (Exception ex)
            {
                await HandleOperationExceptionAsync(ex, "ExecuteRawScalar", typeof(T));
                throw;
            }
        }

        public async Task<T?> QuerySingle<T>(string query, object? parameters = null)
        {
            ThrowIfDisposed();
            Open();
            try
            {
                return await OrmMapper.QuerySingle<T>(query, parameters, _connection, _transaction);
            }
            catch (Exception ex)
            {
                await HandleOperationExceptionAsync(ex, "QuerySingle", typeof(T));
                throw;
            }
        }
        #endregion

        #region Transaction Control
        public async Task CommitAsync()
        {
            ThrowIfDisposed();
            if (_inTransaction && !_error && !_committed)
            {
                if (_transaction is IDbTransaction syncTransaction)
                {
                    syncTransaction.Commit();
                }
                else
                {
                    // For async transactions if your provider supports it
                    _transaction.Commit();
                }
                _committed = true;
                _inTransaction = false;
            }
        }

        public void Commit()
        {
            ThrowIfDisposed();
            if (_inTransaction && !_error && !_committed)
            {
                _transaction?.Commit();
                _committed = true;
                _inTransaction = false;
            }
        }

        public async Task RollbackAsync()
        {
            if (_inTransaction && _transaction != null)
            {
                try
                {
                    if (_transaction is IDbTransaction syncTransaction)
                    {
                        syncTransaction.Rollback();
                    }
                    else
                    {
                        _transaction.Rollback();
                    }
                }
                catch
                {
                    // Ignore rollback errors
                }
                finally
                {
                    _inTransaction = false;
                    _error = true;
                }
            }
        }

        public void Rollback()
        {
            if (_inTransaction && _transaction != null)
            {
                try
                {
                    _transaction.Rollback();
                }
                catch
                {
                    // Ignore rollback errors
                }
                finally
                {
                    _inTransaction = false;
                    _error = true;
                }
            }
        }
        #endregion

        #region Private Methods
        public void Open()
        {
            if (!_connOpened)
            {
                if (_connection.State != ConnectionState.Open)
                {
                    _connection.Open();
                }
                _connOpened = true;
            }

            if (!_inTransaction)
            {
                _transaction = _connection.BeginTransaction(IsolationLevel);
                _inTransaction = true;
                InitializeBulkOptions(); // Reinitialize with transaction
            }
        }

        private void InitializeBulkOptions()
        {
            if (_bulkSaveOptions == null)
            {
                _bulkSaveOptions = new BulkSaveOptions
                {
                    CurrentConnection = _connection,
                    Transaction = _transaction,
                    UseTransaction = false,
                    AuditUserId = _auditUserId,
                    Interceptors = _interceptors
                };
            }
            if (_bulkDeleteOptions == null)
            {
                _bulkDeleteOptions = new BulkDeleteOptions
                {
                    CurrentConnection = _connection,
                    Transaction = _transaction,
                    UseTransaction = false,
                    UserId = _auditUserId,
                    Interceptors = _interceptors
                };
            }
        }

        private async Task HandleOperationExceptionAsync(Exception exception, string operation, Type? entityType = null)
        {
            _error = true;
            await RollbackAsync();
        }

        private void Close()
        {
            if (_connection != null && _connOpened)
            {
                try
                {
                    if (_connection.State == ConnectionState.Open)
                    {
                        _connection.Close();
                    }
                }
                catch
                {
                    // Ignore close errors
                }
                finally
                {
                    _connOpened = false;
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MultiTransaction));
            }
        }
        #endregion

        #region IDisposable Implementation
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore();
            Dispose(false);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    if (_inTransaction && !_error && !_committed)
                    {
                        // Auto-commit if no errors and not explicitly committed/rolled back
                        Commit();
                    }
                    else if (_inTransaction && _error)
                    {
                        Rollback();
                    }
                }
                catch
                {
                    // Ignore disposal errors
                }

                _transaction?.Dispose();
                Close();
                _connection?.Dispose();
                _disposed = true;
            }
        }

        protected virtual async ValueTask DisposeAsyncCore()
        {
            if (!_disposed)
            {
                try
                {
                    if (_inTransaction && !_error && !_committed)
                    {
                        // Auto-commit if no errors and not explicitly committed/rolled back
                        await CommitAsync();
                    }
                    else if (_inTransaction && _error)
                    {
                        await RollbackAsync();
                    }
                }
                catch
                {
                    // Ignore disposal errors
                }

                _transaction?.Dispose();
                Close();
            }
        }
        #endregion
    }
}
