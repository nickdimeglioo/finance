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


//// Main ORM Mapper - Static functions
//public static partial class OrmMapper
//{
    

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



//    //private static object CreateMapperForType(Type type)
//    //{
//    //    var mapperType = typeof(OrmMapper<>).MakeGenericType(type);
//    //    return Activator.CreateInstance(mapperType);
//    //}
//}
////}
