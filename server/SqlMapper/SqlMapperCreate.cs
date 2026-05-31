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



//    public static async Task<object> InsertAsync(ref object entity)
//    {
//        var type = entity.GetType();
//        var method = typeof(OrmMapper).GetMethod(nameof(InsertAsync), new[] { type });
//        var genericMethod = method.MakeGenericMethod(type);
//        entity = await (Task<object>)genericMethod.Invoke(null, new object[] { entity });
//        return entity;
//    }

//}
////}
