//using System;
//using System.Collections;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.ComponentModel.DataAnnotations;
//using System.Data;
//using System.Globalization;
//using System.Linq;
//using System.Reflection;
//using System.Text;
//using System.Threading.Tasks;
//using Dapper;
//using Npgsql;

//// Attributes
//[AttributeUsage(AttributeTargets.Class)]
//public class TableAttribute : Attribute
//{
//    public string Name { get; }
//    public TableAttribute(string name) => Name = name;
//}

//[AttributeUsage(AttributeTargets.Property)]
//public class PrimaryKeyAttribute : Attribute { }

//[AttributeUsage(AttributeTargets.Property)]
//public class ColumnAttribute : Attribute
//{
//    public string Name { get; }
//    public ColumnAttribute(string name) => Name = name;
//}

//[AttributeUsage(AttributeTargets.Property)]
//public class ForeignKeyAttribute : Attribute
//{
//    public string ColumnName { get; }
//    public ForeignKeyAttribute(string columnName) => ColumnName = columnName;
//}

//[AttributeUsage(AttributeTargets.Property)]
//public class NotLoadableAttribute : Attribute { }

//[AttributeUsage(AttributeTargets.Property)]
//public class NotSavableAttribute : Attribute { }

//[AttributeUsage(AttributeTargets.Property)]
//public class NullableAttribute : Attribute { }

//[AttributeUsage(AttributeTargets.Property)]
//public class UniqueAttribute : Attribute { }

//[AttributeUsage(AttributeTargets.Property)]
//public class InversePropertyAttribute : Attribute
//{
//    public string PropertyName { get; }
//    public InversePropertyAttribute(string propertyName) => PropertyName = propertyName;
//}

//// Metadata classes
//public class PropertyMetadata
//{
//    public PropertyInfo Property { get; set; }
//    public string ColumnName { get; set; }
//    public bool IsPrimaryKey { get; set; }
//    public bool IsLoadable { get; set; } = true;
//    public bool IsSavable { get; set; } = true;
//    public bool IsNullable { get; set; }
//    public bool IsUnique { get; set; }
//    public bool IsForeignKey { get; set; }
//    public string ForeignKeyColumn { get; set; }
//    public string ForeignKeyName { get; set; }
//    public Type ForeignKeyType { get; set; }
//    public bool IsCollection { get; set; }
//    public string InverseProperty { get; set; }
//}

//public class ClassMetadata
//{
//    public Type Type { get; set; }
//    public string TableName { get; set; }
//    public PropertyMetadata PrimaryKey { get; set; }
//    public List<PropertyMetadata> Properties { get; set; } = new();
//    public List<PropertyMetadata> LoadableProperties { get; set; } = new();
//    public List<PropertyMetadata> SavableProperties { get; set; } = new();
//    public List<PropertyMetadata> ForeignKeys { get; set; } = new();
//    public List<PropertyMetadata> Collections { get; set; } = new();
//}

//// Connection abstraction


//// Main ORM Mapper - Static functions
//public static class OrmMapper
//{
//    private static readonly ConcurrentDictionary<Type, ClassMetadata> _metadataCache = new();

//    public static ClassMetadata GetMetadata<T>() => GetOrCreateMetadata(typeof(T));
//    public static ClassMetadata GetMetadata(Type type) => GetOrCreateMetadata(type);

//    private static ClassMetadata GetOrCreateMetadata(Type type)
//    {
//        return _metadataCache.GetOrAdd(type, t =>
//        {
//            var metadata = new ClassMetadata { Type = t };

//            // Get table name
//            var tableAttr = t.GetCustomAttribute<TableAttribute>();
//            metadata.TableName = tableAttr?.Name ?? ToSnakeCase($"{t.Name}s"); // defaults to plural name for the table (e.g. class User -> table Users) when no table name given
//            PropertyMetadata? pk = null;
//            string primaryKeyId = $"{ToPascalCase(metadata.TableName)}Id"; // we have an initial guess for primary key in case none is given
//            // Process properties
//            foreach (var prop in t.GetProperties())
//            {
//                bool isCustomPrimaryKey = false;
//                if(pk == null && prop.PropertyType == typeof(int) && prop.Name == primaryKeyId) // we do strict equals / case sensitive on purpose
//                {
//                    isCustomPrimaryKey = true;
//                }
//                var propMetadata = new PropertyMetadata
//                {
//                    Property = prop,
//                    ColumnName = prop.GetCustomAttribute<ColumnAttribute>()?.Name ?? ToSnakeCase(prop.Name),
//                    IsPrimaryKey = prop.GetCustomAttribute<PrimaryKeyAttribute>() != null || isCustomPrimaryKey,
//                    IsLoadable = prop.GetCustomAttribute<NotLoadableAttribute>() == null,
//                    IsSavable = prop.GetCustomAttribute<NotSavableAttribute>() == null,
//                    IsNullable = prop.GetCustomAttribute<NullableAttribute>() != null,
//                    IsUnique = prop.GetCustomAttribute<UniqueAttribute>() != null
//                };

//                // Check for foreign key
//                var fkAttr = prop.GetCustomAttribute<ForeignKeyAttribute>();
//                if (fkAttr != null || (!IsSimpleType(prop.PropertyType) && !IsCollection(prop.PropertyType)))
//                {
//                    propMetadata.IsForeignKey = true;
//                    propMetadata.ForeignKeyColumn = fkAttr?.ColumnName ?? ToSnakeCase(prop.Name) + "_id";
//                    propMetadata.ForeignKeyType = prop.PropertyType;
//                    propMetadata.ForeignKeyName = ToForeignKey(prop.Name);
//                }

//                // Check for collections
//                if (IsCollection(prop.PropertyType))
//                {
//                    propMetadata.IsCollection = true;
//                    propMetadata.InverseProperty = prop.GetCustomAttribute<InversePropertyAttribute>()?.PropertyName;
//                }

//                metadata.Properties.Add(propMetadata);

//                if (propMetadata.IsPrimaryKey)
//                {
//                    metadata.PrimaryKey = propMetadata;
//                    if (!isCustomPrimaryKey && pk != null) // handle overriding PK with one with attribute
//                        pk.IsPrimaryKey = false;
//                    else
//                        pk = propMetadata;
//                }
                    

//                if (propMetadata.IsLoadable)
//                    metadata.LoadableProperties.Add(propMetadata);

//                if (propMetadata.IsSavable)
//                    metadata.SavableProperties.Add(propMetadata);

//                if (propMetadata.IsForeignKey)
//                    metadata.ForeignKeys.Add(propMetadata);

//                if (propMetadata.IsCollection)
//                    metadata.Collections.Add(propMetadata);
//            }

//            return metadata;
//        });
//    }

//    private static bool IsSimpleType(Type type)
//    {
//        return type.IsPrimitive || type.IsEnum || type == typeof(string) ||
//               type == typeof(DateTime) || type == typeof(decimal) || type == typeof(Guid) ||
//               (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) &&
//                IsSimpleType(type.GetGenericArguments()[0]));
//    }

//    private static bool IsCollection(Type type)
//    {
//        return type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
//    }

//    public static string ToPascalCase(string snake_case)
//    {
//        if (string.IsNullOrWhiteSpace(snake_case))
//            return string.Empty;

//        // Split the string by underscores
//        string[] parts = snake_case.Split('_');

//        // Capitalize each part and combine
//        TextInfo textInfo = CultureInfo.InvariantCulture.TextInfo;
//        for (int i = 0; i < parts.Length; i++)
//        {
//            parts[i] = textInfo.ToTitleCase(parts[i].ToLower());
//        }

//        return string.Concat(parts);
//    }

//    private static string ToSnakeCase(string PascalCase)
//    {
//        if (string.IsNullOrEmpty(PascalCase)) return PascalCase;

//        var result = new StringBuilder();
//        result.Append(char.ToLower(PascalCase[0]));

//        for (int i = 1; i < PascalCase.Length; i++)
//        {
//            if (char.IsUpper(PascalCase[i]))
//            {
//                result.Append('_');
//                result.Append(char.ToLower(PascalCase[i]));
//            }
//            else
//            {
//                result.Append(PascalCase[i]);
//            }
//        }

//        return result.ToString();
//    }


//    private static string ToForeignKey(string key)
//    {
//        return $"fk_{key.ToLower()}_id";
//    }
//    public static void SetForeignKeyIds<T>(T entity, dynamic fkData, ClassMetadata metadata)
//    {
//        if (fkData == null || metadata?.ForeignKeys == null)
//            return;

//        foreach (var fkMeta in metadata.ForeignKeys)
//        {
//            try
//            {
//                // Get the value from the dynamic FK data
//                object value = GetDynamicValue(fkData, fkMeta.ForeignKeyName ?? fkMeta.ForeignKeyColumn ?? fkMeta.ColumnName);
//                if (value == null)
//                    continue;

//                var navProp = fkMeta.Property;
//                if (navProp == null || !navProp.CanWrite)
//                    continue;

//                var fkObjectType = navProp.PropertyType;

//                // Try to get the existing FK object from entity
//                var fkObject = navProp.GetValue(entity);
//                if (fkObject == null)
//                {
//                    // Create a new instance of the FK object
//                    fkObject = Activator.CreateInstance(fkObjectType);
//                }

//                // Find the primary key property on the FK object via [PrimaryKey] attribute
//                var fkPrimaryKeyProp = fkObjectType
//                    .GetProperties()
//                    .FirstOrDefault(p => p.GetCustomAttributes(typeof(PrimaryKeyAttribute), true).Any());

//                if (fkPrimaryKeyProp != null && fkPrimaryKeyProp.CanWrite)
//                {
//                    var convertedValue = Convert.ChangeType(value, fkPrimaryKeyProp.PropertyType);
//                    fkPrimaryKeyProp.SetValue(fkObject, convertedValue);

//                    // Assign the FK object back to the entity
//                    navProp.SetValue(entity, fkObject);
//                }
//            }
//            catch
//            {
//                // Optional: logging
//            }
//        }
//    }


//    private static object GetDynamicValue(dynamic dynObj, string columnName)
//    {
//        if (dynObj == null || string.IsNullOrEmpty(columnName))
//            return null;

//        // Try treating as dictionary (common for ExpandoObject or Dapper dynamics)
//        if (dynObj is IDictionary<string, object> dict && dict.ContainsKey(columnName))
//        {
//            return dict[columnName];
//        }

//        // Fallback to reflection for anonymous types or Dapper's internal types
//        var prop = dynObj.GetType().GetProperty(columnName);
//        return prop?.GetValue(dynObj);
//    }




//    // CRUD Operations - Generic typed functions with Dapper multi-mapping
//    // id is the id of object being loaded, 
//    public static async Task<T> GetByIdAsync<T>(object id, int depth = -1, HashSet<Type>? ignoredTypes = null) where T : class, new()
//    {
//        Type type = typeof(T);
//        var metadata = GetOrCreateMetadata(type);
//        var loadableProperties = metadata.LoadableProperties;
//        ignoredTypes ??= [];
//        if (ignoredTypes.Contains(type)) // If type is ignored or we already traversed it, just load the pk into the object, do not full load it
//        {
//            loadableProperties = metadata.LoadableProperties.Where(p => p.IsPrimaryKey).AsList();
//            depth = 0;
//        }
//        ignoredTypes.Add(type);
//        using var connection = DbConnection.UsersConnection;

//        // Build SQL with entity columns + FK ID columns
//        var entityColumns = new List<string>();
//        var fkColumns = new List<string>();

//        foreach (var prop in loadableProperties)
//        {
//            if (prop.IsForeignKey)
//            {
//                fkColumns.Add($"{prop.ForeignKeyColumn} as {prop.ForeignKeyName}");
//            }
//            else if (!prop.IsCollection)
//            {
//                entityColumns.Add($"{prop.ColumnName} as {prop.Property.Name}");
//            }
//        }

//        var allColumns = entityColumns.Concat(fkColumns);
//        var sql = $"SELECT {string.Join(", ", allColumns)} FROM {metadata.TableName} WHERE {metadata.PrimaryKey.ColumnName} = @id";

//        // Use Dapper multi-mapping to get both entity and FK IDs
//        IEnumerable<T> results;

//        if (fkColumns.Any())
//        {
//            var splitColumn = fkColumns.First().Split(" as ")[1];

//            results = await connection.QueryAsync<T, dynamic, T>(
//                sql,
//                (entity, fkData) =>
//                {
//                    SetForeignKeyIds(entity, fkData, metadata);
//                    return entity;
//                },
//                new { id },
//                splitOn: splitColumn
//            );
//        }
//        else
//        {
//            results = await connection.QueryAsync<T>(
//                sql,
//                new { id }
//            );
//        }


//        var entity = results.FirstOrDefault();

//        if (entity != null && depth != 0)
//        {
//            await LoadNavigationPropertiesAsync(connection, entity, metadata, depth - 1, ignoredTypes);
//        }

//        return entity;
//    }

//    public static async Task<IEnumerable<T>> GetAllAsync<T>(int depth = -1, HashSet<Type>? ignoredTypes = null) where T : class, new()
//    {
//        Type type = typeof(T);
//        var metadata = GetOrCreateMetadata(type);
//        using var connection = DbConnection.UsersConnection;
//        // Build SQL with entity columns + FK ID columns
//        var entityColumns = new List<string>();
//        var fkColumns = new List<string>();
//        var loadableProperties = metadata.LoadableProperties;
//        ignoredTypes ??= [];
//        if (ignoredTypes.Contains(type)) // If type is ignored or we already traversed it, just load the pk into the object, do not full load it
//        {
//            loadableProperties = metadata.LoadableProperties.Where(p => p.IsPrimaryKey).AsList();
//            depth = 0;
//        }
//        ignoredTypes.Add(type);

//        foreach (var prop in loadableProperties)
//        {
//            if (prop.IsForeignKey)
//            {
//                fkColumns.Add($"{prop.ForeignKeyColumn} as {prop.ForeignKeyName}");
//            }
//            else if (!prop.IsCollection)
//            {
//                entityColumns.Add(prop.ColumnName);
//            }
//        }

//        var allColumns = entityColumns.Concat(fkColumns);
//        var sql = $"SELECT {string.Join(", ", allColumns)} FROM {metadata.TableName}";

//        IEnumerable<T> results;

//        if (fkColumns.Any())
//        {
//            results = await connection.QueryAsync<T, dynamic, T>(
//                sql,
//                (entity, fkData) =>
//                {
//                    SetForeignKeyIds(entity, fkData, metadata);
//                    return entity;
//                },
//                splitOn: fkColumns.First().Split(' ')[0]
//            );
//        }
//        else
//        {
//            // No FK columns, use simple mapping
//            results = await connection.QueryAsync<T>(sql);
//        }

//        if (depth != 0)
//        {
//            foreach (var result in results)
//            {
//                await LoadNavigationPropertiesAsync(connection, result, metadata, depth - 1, ignoredTypes);
//            }
//        }

//        return results;
//    }

//    public static async Task<T> InsertAsync<T>(T entity) where T : class
//    {
//        var metadata = GetOrCreateMetadata(typeof(T));
//        using var connection = DbConnection.UsersConnection;

//        var savableProps = metadata.SavableProperties.Where(p => !p.IsPrimaryKey && !p.IsForeignKey && !p.IsCollection).ToList();
//        var fkProps = metadata.ForeignKeys.Where(p => p.IsSavable).ToList();

//        var columns = new List<string>();
//        var values = new List<string>();
//        var parameters = new DynamicParameters();

//        foreach (var prop in savableProps)
//        {
//            columns.Add(prop.ColumnName);
//            values.Add($"@{prop.Property.Name}");
//            parameters.Add(prop.Property.Name, prop.Property.GetValue(entity));
//        }

//        foreach (var fkProp in fkProps)
//        {
//            var fkEntity = fkProp.Property.GetValue(entity);
//            if (fkEntity != null)
//            {
//                var fkMetadata = GetOrCreateMetadata(fkProp.ForeignKeyType);
//                var fkId = fkMetadata.PrimaryKey.Property.GetValue(fkEntity);
//                columns.Add(fkProp.ForeignKeyColumn);
//                values.Add($"@{fkProp.Property.Name}_id");
//                parameters.Add($"{fkProp.Property.Name}_id", fkId);
//            }
//        }

//        var sql = $@"
//            INSERT INTO {metadata.TableName} ({string.Join(", ", columns)}) 
//            VALUES ({string.Join(", ", values)}) 
//            RETURNING {metadata.PrimaryKey.ColumnName}";

//        var newId = await connection.QuerySingleAsync<object>(sql, parameters);
//        metadata.PrimaryKey.Property.SetValue(entity, Convert.ChangeType(newId, metadata.PrimaryKey.Property.PropertyType));

//        return entity;
//    }

//    public static async Task<T> UpdateAsync<T>(T entity) where T : class
//    {
//        var metadata = GetOrCreateMetadata(typeof(T));
//        using var connection = DbConnection.UsersConnection;

//        var savableProps = metadata.SavableProperties.Where(p => !p.IsPrimaryKey && !p.IsForeignKey && !p.IsCollection).ToList();
//        var fkProps = metadata.ForeignKeys.Where(p => p.IsSavable).ToList();

//        var setParts = new List<string>();
//        var parameters = new DynamicParameters();

//        foreach (var prop in savableProps)
//        {
//            setParts.Add($"{prop.ColumnName} = @{prop.Property.Name}");
//            parameters.Add(prop.Property.Name, prop.Property.GetValue(entity));
//        }

//        foreach (var fkProp in fkProps)
//        {
//            var fkEntity = fkProp.Property.GetValue(entity);
//            if (fkEntity != null)
//            {
//                var fkMetadata = GetOrCreateMetadata(fkProp.ForeignKeyType);
//                var fkId = fkMetadata.PrimaryKey.Property.GetValue(fkEntity);
//                setParts.Add($"{fkProp.ForeignKeyColumn} = @{fkProp.Property.Name}_id");
//                parameters.Add($"{fkProp.Property.Name}_id", fkId);
//            }
//            else
//            {
//                setParts.Add($"{fkProp.ForeignKeyColumn} = NULL");
//            }
//        }

//        var pkValue = metadata.PrimaryKey.Property.GetValue(entity);
//        parameters.Add("pkValue", pkValue);

//        var sql = $@"
//            UPDATE {metadata.TableName} 
//            SET {string.Join(", ", setParts)} 
//            WHERE {metadata.PrimaryKey.ColumnName} = @pkValue";

//        await connection.ExecuteAsync(sql, parameters);
//        return entity;
//    }

//    public static async Task<bool> DeleteAsync<T>(object id) where T : class
//    {
//        var metadata = GetOrCreateMetadata(typeof(T));
//        using var connection = DbConnection.UsersConnection;

//        var sql = $"DELETE FROM {metadata.TableName} WHERE {metadata.PrimaryKey.ColumnName} = @id";
//        var rowsAffected = await connection.ExecuteAsync(sql, new { id });

//        return rowsAffected > 0;
//    }

//    public static async Task<bool> DeleteAsync<T>(T entity) where T : class
//    {
//        var metadata = GetOrCreateMetadata(typeof(T));
//        var pkValue = metadata.PrimaryKey.Property.GetValue(entity);
//        return await DeleteAsync<T>(pkValue);
//    }

//    // Object wrapper functions
//    public static async Task<object> GetByIdAsync(Type type, object id, int depth = -1, HashSet<Type>? ignoredTypes = null)
//    {
//        ignoredTypes ??= [];
//        var method = typeof(OrmMapper).GetMethod(nameof(GetByIdAsync), new[] { typeof(object), typeof(int), typeof(HashSet<Type>) });
//        var genericMethod = method.MakeGenericMethod(type);

//        // Call the method, which returns a Task<T>
//        var task = (Task)genericMethod.Invoke(null, new object[] { id, depth, ignoredTypes });

//        // Await the task dynamically
//        await task.ConfigureAwait(false);

//        // Use reflection to get the Result property from Task<T>
//        var resultProperty = task.GetType().GetProperty("Result");
//        return resultProperty.GetValue(task);
//    }

//    public static async Task<IEnumerable> GetAllAsync(Type type, int depth = -1)
//    {
//        var method = typeof(OrmMapper).GetMethod(nameof(GetAllAsync), new[] { typeof(int) });
//        var genericMethod = method.MakeGenericMethod(type);
//        return await (Task<IEnumerable>)genericMethod.Invoke(null, new object[] { depth });
//    }

//    //public static async Task<object> InsertAsync(ref object entity)
//    //{
//    //    var type = entity.GetType();
//    //    var method = typeof(OrmMapper).GetMethod(nameof(InsertAsync), new[] { type });
//    //    var genericMethod = method.MakeGenericMethod(type);
//    //    entity = await (Task<object>)genericMethod.Invoke(null, new object[] { entity });
//    //    return entity;
//    //}

//    //public static async Task<object> UpdateAsync(ref object entity)
//    //{
//    //    var type = entity.GetType();
//    //    var method = typeof(OrmMapper).GetMethod(nameof(UpdateAsync), new[] { type });
//    //    var genericMethod = method.MakeGenericMethod(type);
//    //    entity = await (Task<object>)genericMethod.Invoke(null, new object[] { entity });
//    //    return entity;
//    //}

//    //public static async Task<bool> DeleteAsync(Type type, object id)
//    //{
//    //    var method = typeof(OrmMapper).GetMethods()
//    //        .First(m => m.Name == nameof(DeleteAsync) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(object));
//    //    var genericMethod = method.MakeGenericMethod(type);
//    //    return await (Task<bool>)genericMethod.Invoke(null, new object[] { id });
//    //}

//    //public static async Task<bool> DeleteAsync(ref object entity)
//    //{
//    //    var type = entity.GetType();
//    //    var method = typeof(OrmMapper).GetMethods()
//    //        .First(m => m.Name == nameof(DeleteAsync) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == type);
//    //    var genericMethod = method.MakeGenericMethod(type);
//    //    return await (Task<bool>)genericMethod.Invoke(null, new object[] { entity });
//    //}

//    // Helper method to map dynamic result to entity with proper FK handling
//    private static T MapDynamicToEntity<T>(dynamic dynamicResult, ClassMetadata metadata) where T : class, new()
//    {
//        var entity = new T();
//        var resultDict = (IDictionary<string, object>)dynamicResult;

//        foreach (var prop in metadata.LoadableProperties)
//        {
//            if (prop.IsCollection) continue;

//            object value = null;

//            if (prop.IsForeignKey)
//            {
//                // Get the FK ID value from the result
//                if (resultDict.TryGetValue(prop.ForeignKeyColumn, out var fkIdValue) && fkIdValue != null)
//                {
//                    // Create a new instance of the FK entity and set its PK
//                    var fkMetadata = GetOrCreateMetadata(prop.ForeignKeyType);
//                    var fkEntity = Activator.CreateInstance(prop.ForeignKeyType);

//                    // Set the primary key of the foreign entity
//                    var convertedFkId = Convert.ChangeType(fkIdValue, fkMetadata.PrimaryKey.Property.PropertyType);
//                    fkMetadata.PrimaryKey.Property.SetValue(fkEntity, convertedFkId);

//                    prop.Property.SetValue(entity, fkEntity);
//                }
//            }
//            else
//            {
//                // Handle regular properties
//                if (resultDict.TryGetValue(prop.ColumnName, out var propValue))
//                {
//                    if (propValue != null && propValue != DBNull.Value)
//                    {
//                        value = Convert.ChangeType(propValue, prop.Property.PropertyType);
//                        prop.Property.SetValue(entity, value);
//                    }
//                }
//            }
//        }

//        return entity;
//    }
//    private static async Task LoadNavigationPropertiesAsync(IDbConnection connection, object entity, ClassMetadata metadata, int depth, HashSet<Type> ignoredTypes)
//    {
//        if (depth == 0) return;

//        // Load foreign key entities (we already have the FK IDs set, just need to load the full entities)
//        foreach (var fkProp in metadata.ForeignKeys)
//        {
//            if (!fkProp.IsLoadable) continue;

//            var currentFkEntity = fkProp.Property.GetValue(entity);
//            if (currentFkEntity != null)
//            {
//                var fkMetadata = GetOrCreateMetadata(fkProp.ForeignKeyType);
//                var fkId = fkMetadata.PrimaryKey.Property.GetValue(currentFkEntity);

//                if (fkId != null)
//                {
//                    // Load the full FK entity
//                    var loadedFkEntity = await GetByIdAsync(fkProp.ForeignKeyType, fkId, depth - 1, ignoredTypes);
//                    fkProp.Property.SetValue(entity, loadedFkEntity);
//                }
//            }
//        }

//        // Load collections
//        foreach (var collProp in metadata.Collections)
//        {
//            if (!collProp.IsLoadable) continue;

//            var collectionType = collProp.Property.PropertyType;
//            var elementType = collectionType.GetGenericArguments()[0];
//            var collMetadata = GetOrCreateMetadata(elementType);

//            var pkValue = metadata.PrimaryKey.Property.GetValue(entity);
//            var inverseColumn = collProp.InverseProperty != null
//                ? ToSnakeCase(collProp.InverseProperty) + "_id"
//                : ToSnakeCase(metadata.Type.Name) + "_id";

//            // Build columns for collection query
//            var columns = new List<string>();
//            foreach (var prop in collMetadata.LoadableProperties)
//            {
//                if (prop.IsForeignKey)
//                {
//                    columns.Add(prop.ForeignKeyColumn);
//                }
//                else if (!prop.IsCollection)
//                {
//                    columns.Add(prop.ColumnName);
//                }
//            }

//            var collSql = $"SELECT {string.Join(", ", columns)} FROM {collMetadata.TableName} WHERE {inverseColumn} = @pkValue";
//            var collectionResults = await connection.QueryAsync(collSql, new { pkValue });

//            var listType = typeof(List<>).MakeGenericType(elementType);
//            var list = Activator.CreateInstance(listType) as IList;

//            foreach (var dynamicResult in collectionResults)
//            {
//                var item = MapDynamicToEntity(dynamicResult, collMetadata, elementType);

//                if (depth != 0)
//                {
//                    await LoadNavigationPropertiesAsync(connection, item, collMetadata, depth - 1, ignoredTypes);
//                }
//                list.Add(item);
//            }

//            collProp.Property.SetValue(entity, list);
//        }
//    }

//    private static object MapDynamicToEntity(dynamic dynamicResult, ClassMetadata metadata, Type entityType)
//    {
//        var entity = Activator.CreateInstance(entityType);
//        var resultDict = (IDictionary<string, object>)dynamicResult;

//        foreach (var prop in metadata.LoadableProperties)
//        {
//            if (prop.IsCollection) continue;

//            if (prop.IsForeignKey)
//            {
//                // Get the FK ID value from the result
//                if (resultDict.TryGetValue(prop.ForeignKeyColumn, out var fkIdValue) && fkIdValue != null)
//                {
//                    // Create a new instance of the FK entity and set its PK
//                    var fkMetadata = GetOrCreateMetadata(prop.ForeignKeyType);
//                    var fkEntity = Activator.CreateInstance(prop.ForeignKeyType);

//                    // Set the primary key of the foreign entity
//                    var convertedFkId = Convert.ChangeType(fkIdValue, fkMetadata.PrimaryKey.Property.PropertyType);
//                    fkMetadata.PrimaryKey.Property.SetValue(fkEntity, convertedFkId);

//                    prop.Property.SetValue(entity, fkEntity);
//                }
//            }
//            else
//            {
//                // Handle regular properties
//                if (resultDict.TryGetValue(prop.ColumnName, out var propValue))
//                {
//                    if (propValue != null && propValue != DBNull.Value)
//                    {
//                        var value = Convert.ChangeType(propValue, prop.Property.PropertyType);
//                        prop.Property.SetValue(entity, value);
//                    }
//                }
//            }
//        }

//        return entity;
//    }

//    //private static object CreateMapperForType(Type type)
//    //{
//    //    var mapperType = typeof(OrmMapper<>).MakeGenericType(type);
//    //    return Activator.CreateInstance(mapperType);
//    //}
//}
////}

////// Example usage classes
////[Table("users")]
////public class User
////{
////    [PrimaryKey]
////    public int UserId { get; set; }

////    public string Username { get; set; } = "";

////    public string Email { get; set; } = "";

////    [NotSavable]
////    public DateTime LastLogin { get; set; }

////    [InverseProperty("User")]
////    public List<Post> Posts { get; set; } = new();
////}

////[Table("posts")]
////public class Post
////{
////    [PrimaryKey]
////    public int PostId { get; set; }

////    [ForeignKey("user_id")]
////    public User User { get; set; }

////    public string Title { get; set; } = "";

////    public string Content { get; set; } = "";

////    public DateTime CreatedAt { get; set; }

////    [Nullable]
////    public string Summary { get; set; }
////}

////// Usage example
////public class Example
////{
////    public async Task ExampleUsage()
////    {
////        // Using generic typed methods
////        var user = new User { Username = "john_doe", Email = "john@example.com" };
////        await OrmMapper.InsertAsync(user);

////        var post = new Post
////        {
////            User = user,
////            Title = "My First Post",
////            Content = "Hello World!",
////            CreatedAt = DateTime.UtcNow
////        };
////        await OrmMapper.InsertAsync(post);

////        // Load user with posts (depth 2 will load user -> posts -> user for each post)
////        var loadedUser = await OrmMapper.GetByIdAsync<User>(user.UserId, depth: 2);

////        // Update user
////        loadedUser.Email = "newemail@example.com";
////        await OrmMapper.UpdateAsync(loadedUser);

////        // Delete post
////        await OrmMapper.DeleteAsync<Post>(post.PostId);

////        // Using object wrapper methods
////        object userObj = new User { Username = "jane_doe", Email = "jane@example.com" };
////        await OrmMapper.InsertAsync(ref userObj);

////        var loadedUserObj = await OrmMapper.GetByIdAsync(typeof(User), ((User)userObj).UserId);
////        ((User)loadedUserObj).Email = "updated@example.com";
////        await OrmMapper.UpdateAsync(ref loadedUserObj);
////    }
////}