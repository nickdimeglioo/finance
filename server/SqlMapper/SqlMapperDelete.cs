using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

// Delete query classes 
public class DeleteQuery
{
    public bool DeleteThis { get; set; } = true;
    public int Depth { get; set; } = -1; // -1 = unlimited depth
    public Dictionary<string, DeleteQuery> NavigationProperties { get; set; } = new();

    public static DeleteQuery Full() => new() { DeleteThis = true, Depth = -1 };
    public static DeleteQuery Light() => new() { DeleteThis = true, Depth = 0 };
    public static DeleteQuery WithDepth(int depth) => new() { DeleteThis = true, Depth = depth };
    public static DeleteQuery Skip() => new() { DeleteThis = false, Depth = 0 };
}

public class BulkDeleteOptions
{
    public bool UseTransaction { get; set; } = true;
    public DeleteQuery DeleteQuery { get; set; } = DeleteQuery.Full();
    public IDbConnection? CurrentConnection { get; set; }
    public IDbTransaction? Transaction { get; set; }
    public IReadOnlyList<IDbInterceptor>? Interceptors { get; set; }
    public CancellationToken CancellationToken { get; set; } = default;
    public string? UserId { get; set; }
}

public enum DeleteType
{
    Full,    // Delete entity and all related entities (default)
    Light,   // Delete only the main entity record (and its inherited rows)
    Soft     // Mark as deleted (requires SoftDelete attribute)
}

public static partial class OrmMapper
{
    // -------------------------------------------------------------------------
    // 1. PUBLIC ENTRY POINTS
    // -------------------------------------------------------------------------

    /// <summary>
    /// Unified Delete Entry Point. Handles Transaction setup and calls the Core Recursive Logic.
    /// </summary>
    public static async Task<bool> DeleteAsync<T>(
        T entity,
        DeleteType deleteType = DeleteType.Full,
        DeleteQuery? deleteQuery = null,
        IDbConnection? conn = null,
        IDbTransaction? trans = null,
        bool useTransaction = true,
        string userId = "System",
        IReadOnlyList<IDbInterceptor>? interceptors = null,
        CancellationToken cancellationToken = default) where T : class, new()
    {
        if (entity == null) return false;

        var options = new BulkDeleteOptions
        {
            DeleteQuery = deleteQuery ?? (deleteType == DeleteType.Light ? DeleteQuery.Light() : DeleteQuery.Full()),
            UseTransaction = useTransaction,
            CurrentConnection = conn,
            Transaction = trans,
            UserId = userId,
            Interceptors = interceptors,
            CancellationToken = cancellationToken
        };

        // Standardize connection handling
        var connection = options.CurrentConnection ?? DbConnection.UsersConnection;
        bool externalConn = options.CurrentConnection != null;

        try
        {
            if (!externalConn && connection.State != ConnectionState.Open) connection.Open();

            IDbTransaction transaction = options.Transaction;
            IDbTransaction localTransaction = null;

            // Start Transaction if needed
            if (options.UseTransaction && transaction == null)
            {
                localTransaction = connection.BeginTransaction();
                transaction = localTransaction;
            }

            // Assign active transaction to options to pass down recursive chain
            options.CurrentConnection = connection;
            options.Transaction = transaction;

            try
            {
                // Handle Soft Delete specifically (it is an Update, not a Delete)
                var metadata = GetMetadata(entity.GetType());
                if (deleteType == DeleteType.Soft || metadata.SupportsSoftDelete)
                {
                    bool result = await SoftDeleteInternalAsync(entity, metadata, options, deleteType);
                    if (localTransaction != null) localTransaction.Commit();
                    return result;
                }

                // Call Core Recursive Logic
                await DeleteInternalAsync(entity, options);

                if (localTransaction != null) localTransaction.Commit();
                return true;
            }
            catch
            {
                if (localTransaction != null) localTransaction.Rollback();
                throw;
            }
            finally
            {
                if (localTransaction != null) localTransaction.Dispose();
            }
        }
        finally
        {
            if (!externalConn) connection.Dispose();
        }
    }

    /// <summary>
    /// Deletes by ID by fetching the entity first so recursive delete logic has the loaded graph.
    /// </summary>
    public static async Task<bool> DeleteByIdAsync<T>(
        object id,
        DeleteType deleteType = DeleteType.Full,
        DeleteQuery? deleteQuery = null,
        IDbConnection? conn = null,
        IDbTransaction? trans = null,
        bool useTransaction = true,
        string userId = "System",
        IReadOnlyList<IDbInterceptor>? interceptors = null,
        CancellationToken cancellationToken = default) where T : class, new()
    {
        if (id == null) return false;

        // Fetch the entity first. We need the object to find its child foreign keys.
        var depth = deleteQuery?.Depth ?? (deleteType == DeleteType.Light ? 0 : -1);

        // Note: Assuming GetByIdAsync exists in your OrmMapper partial
        var entity = await GetByIdAsync<T>(id, depth, selectQuery: new SelectQuery { CurrentConnection = conn, CurrentTransaction = trans, CancellationToken = cancellationToken });

        if (entity == null) return false;

        return await DeleteAsync(entity, deleteType, deleteQuery, conn, trans, useTransaction, userId, interceptors, cancellationToken);
    }

    /// <summary>
    /// Bulk delete iterates the list and calls the core delete for each.
    /// </summary>
    public static async Task<int> DeleteBulkAsync<T>(IEnumerable<T> entities, BulkDeleteOptions? options = null) where T : class, new()
    {
        if (entities == null || !entities.Any()) return 0;
        options ??= new BulkDeleteOptions();

        var connection = options.CurrentConnection ?? DbConnection.UsersConnection;
        bool externalConn = options.CurrentConnection != null;

        try
        {
            if (!externalConn && connection.State != ConnectionState.Open) connection.Open();

            IDbTransaction transaction = options.Transaction;
            IDbTransaction localTransaction = null;

            if (options.UseTransaction && transaction == null)
            {
                localTransaction = connection.BeginTransaction();
                transaction = localTransaction;
            }

            options.CurrentConnection = connection;
            options.Transaction = transaction;

            try
            {
                int count = 0;
                foreach (var entity in entities)
                {
                    var metadata = GetMetadata(entity.GetType());
                    if (metadata.SupportsSoftDelete)
                    {
                        if (await SoftDeleteInternalAsync(entity, metadata, options, DeleteType.Soft))
                            count++;
                        continue;
                    }

                    await DeleteInternalAsync(entity, metadata, options);
                    count++;
                }

                if (localTransaction != null) localTransaction.Commit();
                return count;
            }
            catch
            {
                if (localTransaction != null) localTransaction.Rollback();
                throw;
            }
            finally
            {
                if (localTransaction != null) localTransaction.Dispose();
            }
        }
        finally
        {
            if (!externalConn) connection.Dispose();
        }
    }

    // -------------------------------------------------------------------------
    // 2. CORE RECURSIVE LOGIC
    // -------------------------------------------------------------------------

    /// <summary>
    /// The Core Worker. 
    /// Flow: Delete Self -> Delete Base (Inheritance) -> Delete FKs (Recursion)
    /// </summary>
    public static async Task DeleteInternalAsync<T>(T entity, BulkDeleteOptions options) where T : class
    {
        if (entity == null) return;
        var metadata = GetMetadata(entity.GetType());
        await DeleteInternalAsync(entity, metadata, options);
    }

    private static async Task DeleteInternalAsync(object entity, ClassMetadata metadata, BulkDeleteOptions options)
    {
        var context = new DeleteContext(
            options.CurrentConnection!,
            options.Transaction,
            metadata,
            new[] { entity },
            DeleteType.Full,
            isSoftDelete: false);
        await RunBeforeDeleteInterceptorsAsync(context, options.Interceptors, options.CancellationToken);

        // Delete THIS Table Record
        // We delete the specific derived table record first.
        // In standard SQL inheritance (TPT), you delete the Child (Derived) row, then the Parent (Base) row.
        var pkValue = metadata.PrimaryKey.Property.GetValue(entity);
        string deleteSql = $"DELETE FROM \"{metadata.TableName}\" WHERE \"{metadata.PrimaryKey.ColumnName}\" = @Id";

        await options.CurrentConnection.ExecuteAsync(new CommandDefinition(deleteSql, new { Id = pkValue }, options.Transaction, cancellationToken: options.CancellationToken));
        await RunAfterDeleteInterceptorsAsync(context, options.Interceptors, options.CancellationToken);

        // 3. Delete Base (Inheritance Recursion)
        // "Delete from inherited until no more inheritance"
        if (metadata.BaseMetaData != null)
        {
            var baseMetadata = GetMetadata(metadata.BaseMetaData.ForeignKeyType);
            await DeleteInternalAsync(entity, baseMetadata, options);
        }

        // 4. Delete Foreign Keys (Cascade Recursion)
        // "After object is deleted, delete its foreign key props"
        // Only if Depth allows
        if (options.DeleteQuery.Depth != 0)
        {
            var nextDepthOptions = new BulkDeleteOptions
            {
                CurrentConnection = options.CurrentConnection,
                Transaction = options.Transaction,
                UserId = options.UserId,
                UseTransaction = false, // Already in one
                DeleteQuery = DeleteQuery.WithDepth(options.DeleteQuery.Depth - 1),
                Interceptors = options.Interceptors,
                CancellationToken = options.CancellationToken
            };

            foreach (var fkProp in metadata.ForeignKeys)
            {
                if (!fkProp.IsDeletable || fkProp.IsInherited) continue;

                // Determine if we should delete this specific property based on Query
                var navQuery = options.DeleteQuery.NavigationProperties.GetValueOrDefault(fkProp.Property.Name);
                if (navQuery != null && !navQuery.DeleteThis) continue;

                // Get the value of the property (The FK Entity)
                var childEntity = fkProp.Property.GetValue(entity);

                if (childEntity != null)
                {
                    var childMetadata = GetMetadata(childEntity.GetType());
                    await DeleteInternalAsync(childEntity, childMetadata, nextDepthOptions);
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // 3. HELPER METHODS
    // -------------------------------------------------------------------------

    private static async Task<bool> SoftDeleteInternalAsync<T>(T entity, ClassMetadata metadata, BulkDeleteOptions options, DeleteType deleteType)
    {
        var pkValue = metadata.PrimaryKey.Property.GetValue(entity);

        var softDeleteField = metadata.SoftDeleteField;

        if (string.IsNullOrEmpty(softDeleteField))
            throw new InvalidOperationException($"No SoftDelete configuration found for type {metadata.Type.Name}");

        var sql = $"UPDATE \"{metadata.TableName}\" SET \"{softDeleteField}\" = TRUE WHERE \"{metadata.PrimaryKey.ColumnName}\" = @pkValue";

        var context = new DeleteContext(
            options.CurrentConnection!,
            options.Transaction,
            metadata,
            new object[] { entity! },
            deleteType,
            isSoftDelete: true);
        await RunBeforeDeleteInterceptorsAsync(context, options.Interceptors, options.CancellationToken);

        var rows = await options.CurrentConnection.ExecuteAsync(new CommandDefinition(sql, new { pkValue }, options.Transaction, cancellationToken: options.CancellationToken));
        if (rows > 0 && metadata.SoftDeleteProperty != null)
        {
            metadata.SoftDeleteProperty.Property.SetValue(entity, true);
        }

        await RunAfterDeleteInterceptorsAsync(context, options.Interceptors, options.CancellationToken);
        return rows > 0;
    }
}
