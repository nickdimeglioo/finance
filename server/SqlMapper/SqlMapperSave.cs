using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

public class SaveQuery
{
    public bool SaveThis { get; set; } = true;
    public int Depth { get; set; } = -1; // -1 = unlimited depth
    public bool Upsert { get; set; } = true;
    public Dictionary<string, SaveQuery> NavigationProperties { get; set; } = new();

    public static SaveQuery Full() => new() { SaveThis = true, Depth = -1 };
    public static SaveQuery Light() => new() { SaveThis = true, Depth = 0 };
    public static SaveQuery WithDepth(int depth) => new() { SaveThis = true, Depth = depth };
    public static SaveQuery Skip() => new() { SaveThis = false, Depth = 0 };
}


public static partial class OrmMapper
{
    private static readonly Dictionary<Type, int> _typeParameterCounts = new();
    public static IBulkDialect BulkDialect { get; set; } = new PostgresBulkDialect();
    public static int BulkOperationThreshold { get; set; } = 1_000_00; // Trigger BulkDialect only if > 50k

    #region Public API

    public static async Task<T> SaveAsync<T>(T entity, SaveQuery? saveQuery = null, bool useTransaction = true, 
                                             string auditUserId = "System",
                                             IDbConnection? conn = null, IDbTransaction? trans = null,
                                             IReadOnlyList<IDbInterceptor>? interceptors = null,
                                             CancellationToken cancellationToken = default) where T : class
    {
        if (entity == null) return default;

        var options = new BulkSaveOptions
        {
            SaveQuery = saveQuery ?? SaveQuery.Full(),
            UseTransaction = useTransaction,
            CurrentConnection = conn,
            Transaction = trans,
            AuditUserId = auditUserId,
            Interceptors = interceptors,
            CancellationToken = cancellationToken
        };

        var result = await SaveBulkAsync(new[] { entity }, options);
        return result.FirstOrDefault();
    }

    public static async Task<IEnumerable<T>> SaveBulkAsync<T>(IEnumerable<T> entities, BulkSaveOptions? options = null) where T : class
    {
        if (entities == null) return Enumerable.Empty<T>();
        var entityList = entities as List<T> ?? entities.ToList();
        if (!entityList.Any()) return entityList;

        options ??= new BulkSaveOptions();

        var connection = options.CurrentConnection ?? DbConnection.UsersConnection;
        bool isExternalConnection = options.CurrentConnection != null;

        try
        {
            if (!isExternalConnection && connection.State != ConnectionState.Open)
                connection.Open();

            if (options.UseTransaction && options.Transaction == null)
            {
                using var transaction = connection.BeginTransaction();
                try
                {
                    options.Transaction = transaction;
                    await SaveBulkInternalAsync(connection, entityList, options);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
                finally { options.Transaction = null; }
            }
            else
            {
                await SaveBulkInternalAsync(connection, entityList, options);
            }

            return entityList;
        }
        finally
        {
            if (!isExternalConnection) connection.Dispose();
        }
    }

    #endregion

    #region Internal Core

    private static async Task SaveBulkInternalAsync<T>(IDbConnection connection, List<T> entities, BulkSaveOptions options) where T : class
    {
        var metadata = GetOrCreateMetadata(typeof(T));

        // 1. Recursive Foreign Key Saving
        if (options.SaveQuery.Depth != 0)
        {
            await ProcessForeignKeysAsync(connection, entities, metadata, options);
        }

        // 2. Save Main Entities
        if (options.SaveQuery.SaveThis)
        {
            // CHECK: Is this a "True Bulk" candidate?
            // UPDATED LOGIC: Only use BulkDialect if Count > Threshold (50k)
            bool useBulkDialect = connection is Npgsql.NpgsqlConnection 
                                  && BulkDialect != null 
                                  && entities.Count >= BulkOperationThreshold; 

            if (useBulkDialect)
            {
                var insertContext = CreateInsertContext(connection, entities.Where(entity => IsDefaultValue(metadata.PrimaryKey.Property.GetValue(entity))).Cast<object>().ToList(), metadata, options);
                var updateContext = CreateUpdateContext(connection, entities.Where(entity => !IsDefaultValue(metadata.PrimaryKey.Property.GetValue(entity))).Cast<object>().ToList(), metadata, options);

                if (insertContext.Entities.Count > 0)
                    await RunBeforeInsertInterceptorsAsync(insertContext, options.Interceptors, options.CancellationToken);
                if (updateContext.Entities.Count > 0)
                    await RunBeforeUpdateInterceptorsAsync(updateContext, options.Interceptors, options.CancellationToken);

                await BulkDialect.BulkMergeAsync(connection, options.Transaction, entities, metadata, options);

                if (insertContext.Entities.Count > 0)
                    await RunAfterInsertInterceptorsAsync(insertContext, options.Interceptors, options.CancellationToken);
                if (updateContext.Entities.Count > 0)
                    await RunAfterUpdateInterceptorsAsync(updateContext, options.Interceptors, options.CancellationToken);
            }
            else
            {
                await ProcessEntityBatchAsync(connection, entities, metadata, options);
            }
        }
    }

    private static async Task ProcessEntityBatchAsync<T>(IDbConnection connection, List<T> entities, ClassMetadata metadata, BulkSaveOptions options) where T : class
    {
        var inserts = new List<T>();
        var upserts = new List<T>();

        foreach (var entity in entities)
        {
            var pkValue = metadata.PrimaryKey.Property.GetValue(entity);
            if (IsDefaultValue(pkValue))
            {
                if (metadata.PrimaryKey.Property.PropertyType == typeof(Guid))
                {
                    metadata.PrimaryKey.Property.SetValue(entity, Guid.NewGuid());
                }

                inserts.Add(entity);
            }
            else
            {
                upserts.Add(entity);
            }
        }

        var props = metadata.SavableProperties.Where(p => !p.IsPrimaryKey).ToList();
        int paramsPerEntity = props.Count + 1;
        int batchSize = Math.Max(1, options.ParameterLimit / paramsPerEntity);

        // Execute Inserts
        if (inserts.Any())
        {
            for (int i = 0; i < inserts.Count; i += batchSize)
            {
                var batch = inserts.Skip(i).Take(batchSize).ToList();
                var context = CreateInsertContext(connection, batch.Cast<object>().ToList(), metadata, options);
                await RunBeforeInsertInterceptorsAsync(context, options.Interceptors, options.CancellationToken);
                await ExecuteBatchAsync(connection, batch, metadata, props, options.Transaction, isUpsert: false, options.CancellationToken);
                await RunAfterInsertInterceptorsAsync(context, options.Interceptors, options.CancellationToken);
            }
        }

        // Execute Upserts
        if (upserts.Any())
        {
            for (int i = 0; i < upserts.Count; i += batchSize)
            {
                var batch = upserts.Skip(i).Take(batchSize).ToList();
                var context = CreateUpdateContext(connection, batch.Cast<object>().ToList(), metadata, options);
                await RunBeforeUpdateInterceptorsAsync(context, options.Interceptors, options.CancellationToken);
                await ExecuteBatchAsync(connection, batch, metadata, props, options.Transaction, isUpsert: true, options.CancellationToken);
                await RunAfterUpdateInterceptorsAsync(context, options.Interceptors, options.CancellationToken);
            }
        }
    }

    private static InsertContext CreateInsertContext(IDbConnection connection, List<object> entities, ClassMetadata metadata, BulkSaveOptions options)
    {
        return new InsertContext(connection, options.Transaction, metadata, entities);
    }

    private static UpdateContext CreateUpdateContext(IDbConnection connection, List<object> entities, ClassMetadata metadata, BulkSaveOptions options)
    {
        return new UpdateContext(connection, options.Transaction, metadata, entities);
    }

    private static async Task ExecuteBatchAsync<T>(IDbConnection connection, List<T> batch, ClassMetadata metadata, List<PropertyMetadata> savableProps, IDbTransaction transaction, bool isUpsert, CancellationToken cancellationToken) where T : class
    {
        if (!batch.Any()) return;

        var sb = new StringBuilder();
        var parameters = new DynamicParameters();
        var columnNames = new List<string>();
        var includePrimaryKeyForInsert = !isUpsert && batch.Any(entity => !IsDefaultValue(metadata.PrimaryKey.Property.GetValue(entity)));

        // 1. Setup Columns
        if (isUpsert || includePrimaryKeyForInsert) columnNames.Add($"\"{metadata.PrimaryKey.ColumnName}\"");

        foreach (var prop in savableProps)
        {
            columnNames.Add($"\"{(prop.IsForeignKey ? prop.ForeignKeyColumn : prop.ColumnName)}\"");
        }

        sb.Append($"INSERT INTO \"{metadata.TableName}\" ({string.Join(", ", columnNames)}) VALUES ");

        // 2. Build Values Clause
        var valueRows = new List<string>();
        for (int i = 0; i < batch.Count; i++)
        {
            var entity = batch[i];
            var rowParams = new List<string>();

            // Handle PK for Upsert or caller/client generated insert IDs.
            if (isUpsert || includePrimaryKeyForInsert)
            {
                var pkVal = metadata.PrimaryKey.Property.GetValue(entity);
                var pkParam = $"@pk_{i}";
                rowParams.Add(pkParam);
                parameters.Add(pkParam, pkVal);
            }

            // Handle Other Properties
            foreach (var prop in savableProps)
            {
                var paramName = $"@p_{i}_{rowParams.Count}";
                object val = null;

                if (prop.IsForeignKey && !prop.IsInherited)
                {
                    var fkEntity = prop.Property.GetValue(entity);
                    if (fkEntity != null)
                    {
                        var fkMeta = GetOrCreateMetadata(prop.ForeignKeyType);
                        val = fkMeta.PrimaryKey.Property.GetValue(fkEntity);
                    }
                }
                else
                {
                    val = GetSavableValue(entity, prop);
                }

                // Apply Custom Field formatting if needed (e.g., PostGIS geometry)
                if (prop.CustomField != null)
                    rowParams.Add(mapPropertySingle(paramName, prop.CustomField));
                else
                    rowParams.Add(paramName);

                parameters.Add(paramName, val);
            }

            valueRows.Add($"({string.Join(", ", rowParams)})");
        }

        sb.Append(string.Join(", ", valueRows));

        // 3. Handle Conflict (Upsert) or Return ID (Insert)
        if (isUpsert)
        {
            // ON CONFLICT (id) DO UPDATE SET col = EXCLUDED.col ...
            sb.Append($" ON CONFLICT (\"{metadata.PrimaryKey.ColumnName}\") DO UPDATE SET ");

            var updates = savableProps.Select(p =>
            {
                var col = p.IsForeignKey ? p.ForeignKeyColumn : p.ColumnName;
                return $"\"{col}\" = EXCLUDED.\"{col}\"";
            });

            sb.Append(string.Join(", ", updates));
        }
        else
        {
            // Only need to return IDs for Inserts (Upserts already have them)
            // Note: If you want DB-generated IDs for Upserts that turned out to be new, 
            // you'd need RETURNING here too, but usually Upsert implies known ID.
            sb.Append($" RETURNING \"{metadata.PrimaryKey.ColumnName}\"");
        }

        // 4. Execute and Map IDs back
        if (!isUpsert)
        {
            // We assume the DB returns IDs in the same order as VALUES
            // This is guaranteed in Postgres for INSERT ... RETURNING
            var ids = await connection.QueryAsync<object>(new CommandDefinition(sb.ToString(), parameters, transaction, cancellationToken: cancellationToken));
            var idList = ids.ToList();

            for (int i = 0; i < batch.Count && i < idList.Count; i++)
            {
                var idVal = idList[i];
                // Handle Dapper returning dynamic object wrapping the value
                if (idVal is IDictionary<string, object> dict && dict.Count > 0)
                    idVal = dict.Values.First();

                SetEntityId(batch[i], metadata, idVal);
            }
        }
        else
        {
            await connection.ExecuteAsync(new CommandDefinition(sb.ToString(), parameters, transaction, cancellationToken: cancellationToken));
        }
    }

    #endregion

    #region Foreign Key Logic

    private static async Task ProcessForeignKeysAsync<T>(IDbConnection connection, List<T> entities, ClassMetadata metadata, BulkSaveOptions options)
    {
        var visitedTypes = new HashSet<Type>(); // Local scope for this batch

        foreach (var fkProp in metadata.ForeignKeys.Where(x => x.IsSavable))
        {
            var fkSaveQuery = GetNavigationPropertySaveQuery(options.SaveQuery, fkProp.Property.Name);
            if (!fkSaveQuery.SaveThis && fkSaveQuery.Depth == 0) continue;

            // 1. Collect all distinct FK entities from the batch
            var distinctFkObjects = new Dictionary<object, object>(); // Key=Instance, Value=Instance
            var parentMap = new List<(T parent, object fkInstance)>();

            if (!fkProp.IsInherited)
            {
                foreach (var entity in entities)
                {
                    var val = fkProp.Property.GetValue(entity);
                    if (val != null)
                    {
                        if (!distinctFkObjects.ContainsKey(val))
                            distinctFkObjects[val] = val;

                        parentMap.Add((entity, val));
                    }
                }
            }
           

            if (!fkProp.IsInherited && !distinctFkObjects.Any()) continue;

            var fkList = fkProp.IsInherited ? entities.Cast<object>().ToList() : distinctFkObjects.Values.ToList();
            var fkType = fkProp.ForeignKeyType;

            // 2. Prepare options for the recursive call
            var fkOptions = new BulkSaveOptions
            {
                ParameterLimit = options.ParameterLimit,
                UseTransaction = false, // Inherit transaction
                Transaction = options.Transaction,
                CurrentConnection = connection,
                SaveQuery = fkSaveQuery,
                Interceptors = options.Interceptors,
                AuditUserId = options.AuditUserId,
                CancellationToken = options.CancellationToken
            };

            // 3. Dynamically call SaveBulkInternalAsync<FKType>
            // We use reflection because we don't know FKType at compile time here
            await CallGenericSaveBulk(connection, fkList, fkType, fkOptions);

            // 4. Map saved FK IDs back to parents
            // (Note: Since objects are references, if the save updated the ID on the object in-place, 
            // we don't strictly need to re-set the property, but if the FK property logic
            // relies on changing the instance, this ensures safety).
            foreach (var map in parentMap)
            {
                // Ensure the parent property points to the (possibly updated) FK instance
                fkProp.Property.SetValue(map.parent, map.fkInstance);
            }
        }
    }

    private static Task CallGenericSaveBulk(IDbConnection conn, List<object> entities, Type entityType, BulkSaveOptions options)
    {
        // Convert List<object> to List<ActualType> using reflection magic or dynamic
        var method = typeof(OrmMapper).GetMethod(nameof(SaveBulkInternalAsync), BindingFlags.NonPublic | BindingFlags.Static)
                                      .MakeGenericMethod(entityType);

        // We need to cast List<object> to List<FKType>
        var listType = typeof(List<>).MakeGenericType(entityType);
        var typedList = (IList)Activator.CreateInstance(listType);

        foreach (var item in entities) typedList.Add(item);

        return (Task)method.Invoke(null, new object[] { conn, typedList, options });
    }

    #endregion

    #region Helpers


    static Array NormalizeIds(List<object?> ids)
    {
        if (ids is null || ids.Count == 0)
            throw new ArgumentException("Ids cannot be empty.");

        // Find first non-null value to determine type
        var firstNonNull = ids.FirstOrDefault(x => x is not null);

        if (firstNonNull is null)
            throw new InvalidOperationException(
                "Ids cannot all be null.");

        var elementType = firstNonNull.GetType();

        // Ensure all non-null ids are same type
        if (ids.Any(x => x is not null && x.GetType() != elementType))
        {
            throw new InvalidOperationException(
                "All ids must be the same type.");
        }

        var typedArray = Array.CreateInstance(elementType, ids.Count);

        for (int i = 0; i < ids.Count; i++)
        {
            typedArray.SetValue(ids[i], i);
        }

        return typedArray;
    }
    private static SaveQuery GetNavigationPropertySaveQuery(SaveQuery parentQuery, string propertyName)
    {
        if (parentQuery.NavigationProperties.TryGetValue(propertyName, out var specificQuery))
            return specificQuery;

        var newDepth = parentQuery.Depth > 0 ? parentQuery.Depth - 1 : parentQuery.Depth;
        return new SaveQuery { SaveThis = true, Depth = newDepth };
    }

    private static bool IsDefaultValue(object value)
    {
        if (value == null) return true;
        var type = value.GetType();
        if (type.IsValueType)
            return value.Equals(Activator.CreateInstance(type));

        // String/Ref types
        if (value is string s) return string.IsNullOrEmpty(s);
        return false;
    }

    private static object? GetSavableValue<T>(T entity, PropertyMetadata prop)
    {
        object? value = prop.Property.GetValue(entity);
        if (prop.Converter == null) return value;
        return prop.Converter.FromProvider(value);
    }

    public static void SetEntityId<T>(T entity, ClassMetadata metadata, object idValue)
    {
        if (idValue == null) return;

        var pkProp = metadata.PrimaryKey.Property;
        var pkType = pkProp.PropertyType;

        // Convert types if needed (e.g. long to int, or int to long)
        if (pkType == typeof(int) && idValue is long l) pkProp.SetValue(entity, (int)l);
        else if (pkType == typeof(long) && idValue is int i) pkProp.SetValue(entity, (long)i);
        else if (pkType == typeof(Guid) && idValue is string s) pkProp.SetValue(entity, Guid.Parse(s));
        else pkProp.SetValue(entity, idValue);
    }


    #endregion
}
