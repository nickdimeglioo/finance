using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Dapper;

// Query Builder Extension for OrmMapper
public static partial class OrmMapper
{
    // Entry point for queryable operations
    public static OrmQueryable<T> QueryGet<T>(SelectQuery? selectQuery = null) where T : class, new()
    {
        return new OrmQueryable<T>(selectQuery);
    }
}

// Main queryable class
public class OrmQueryable<T> where T : class, new()
{
    private readonly List<WhereCondition> _conditions = new();
    private readonly List<object> _parameters = new();
    private int _parameterIndex = 0;
    private SelectQuery? _selectQuery = null;

    public OrmQueryable() { }

    public OrmQueryable(SelectQuery? selectQuery)
    {
        if (selectQuery != null) this._selectQuery = selectQuery;
    }

    public OrmQueryable<T> Where(Expression<Func<T, bool>> predicate)
    {
        var metadata = OrmMapper.GetMetadata<T>();
        // Pass metadata to visitor to handle converters
        var visitor = new WhereExpressionVisitor(_parameters, _parameterIndex, metadata);
        var condition = visitor.Visit(predicate.Body);
        _conditions.Add(new WhereCondition { Sql = condition, Parameters = visitor.Parameters });
        _parameterIndex = visitor.ParameterIndex;
        return this;
    }

    // New Overload: Raw SQL with anonymous object parameters
    public OrmQueryable<T> Where(string sql, object? parameters = null)
    {
        if (parameters != null)
        {
            foreach (var prop in parameters.GetType().GetProperties())
            {
                var val = prop.GetValue(parameters);
                _parameters.Add(new KeyValuePair<string, object>(prop.Name, val));
            }
        }

        _conditions.Add(new WhereCondition { Sql = sql });
        return this;
    }

    public async Task<List<T>> ToListAsync(
    int depth = -1,
    HashSet<Type>? ignoredTypes = null,
    int batchSize = 1000,
    SelectQuery? selectQuery = null)
    {
        var metadata = OrmMapper.GetMetadata<T>();
        ignoredTypes ??= new HashSet<Type>();

        if (ignoredTypes.Contains(typeof(T)))
        {
            depth = 0;
        }
        ignoredTypes.Add(typeof(T));

        selectQuery ??= _selectQuery;

        var providedConnection = selectQuery?.CurrentConnection;
        var connection = providedConnection ?? DbConnection.UsersConnection;
        var ownsConnection = providedConnection == null || selectQuery?.ManageConnectionLifecycle == true;
        var externalConnection = !ownsConnection;
        var externalTransaction = selectQuery?.CurrentTransaction != null;

        try
        {
            if (!externalConnection && connection.State != ConnectionState.Open)
                connection.Open();

            if (selectQuery?.UseTransaction == true && !externalTransaction)
            {
                using var transaction = connection.BeginTransaction();
                try
                {
                    selectQuery.CurrentTransaction = transaction;

                    var results = await ToListInternalAsync(
                        connection,
                        metadata,
                        depth,
                        ignoredTypes,
                        selectQuery);

                    transaction.Commit();
                    return results;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            else
            {
                return await ToListInternalAsync(
                    connection,
                    metadata,
                    depth,
                    ignoredTypes,
                    selectQuery);
            }
        }
        finally
        {
            if (!externalConnection)
                connection.Dispose();
        }
    }

    private async Task<List<T>> ToListInternalAsync(
        IDbConnection connection,
        ClassMetadata metadata,
        int depth,
        HashSet<Type> ignoredTypes,
        SelectQuery? selectQuery)
    {
        if (depth == 0)
        {
            return (await GetBatchSimpleWithWhere(connection, metadata, selectQuery)).ToList();
        }
        else
        {
            return (await GetBatchWithJoinsAndWhere(connection, metadata, depth, ignoredTypes, selectQuery)).ToList();
        }
    }

    public async Task<T?> FirstOrDefaultAsync(int depth = -1, HashSet<Type>? ignoredTypes = null, SelectQuery? selectQuery = null)
    {
        selectQuery ??= _selectQuery;
        var results = await ToListAsync(depth, ignoredTypes, 1, selectQuery);
        return results.FirstOrDefault();
    }

    private async Task<IEnumerable<T>> GetBatchSimpleWithWhere(System.Data.IDbConnection connection, ClassMetadata metadata, SelectQuery? selectQuery)
    {
        var columns = metadata.LoadableProperties
            .Where(p => !p.IsForeignKey && OrmMapper.IsSimpleType(p.Property.PropertyType))
            .Select(p => $"\"{p.ColumnName}\" as \"{p.Property.Name}\"");

        var sql = $"SELECT {string.Join(", ", columns)} FROM \"{metadata.TableName}\"";

        var aliasMapping = new Dictionary<string, string>
        {
            { $"{{BASE_TABLE}}", $"\"{metadata.TableName}\"" }
        };

        var whereClause = BuildWhereClause(aliasMapping);

        if (!string.IsNullOrEmpty(whereClause))
        {
            sql += $" WHERE {whereClause}";
        }
        if (OrmMapper.ShouldFilterSoftDeleted(metadata, selectQuery))
        {
            sql += string.IsNullOrEmpty(whereClause)
                ? $" WHERE {OrmMapper.BuildSoftDeletePredicate($"\"{metadata.TableName}\"", metadata)}"
                : $" AND {OrmMapper.BuildSoftDeletePredicate($"\"{metadata.TableName}\"", metadata)}";
        }
        if (selectQuery != null && selectQuery.LockRow)
        {
            sql += " FOR UPDATE" + (selectQuery.NoWait ? " NOWAIT" : string.Empty);
        }
        var parameters = BuildParameterObject();
        var context = new SelectContext(connection, selectQuery?.CurrentTransaction, metadata, selectQuery)
        {
            Sql = sql,
            Parameters = parameters
        };
        await OrmMapper.RunBeforeSelectInterceptorsAsync(context, selectQuery?.Interceptors);
        var results = (await connection.QueryAsync<T>(sql, parameters, selectQuery?.CurrentTransaction)).ToList();
        context.Result = results;
        await OrmMapper.RunAfterSelectInterceptorsAsync(context, selectQuery?.Interceptors);
        return results;
    }

    private async Task<IEnumerable<T>> GetBatchWithJoinsAndWhere(System.Data.IDbConnection connection, ClassMetadata metadata, int depth, HashSet<Type> ignoredTypes, SelectQuery? selectQuery)
    {
        var (baseSql, splitMapping, joinTables) = BuildOptimizedSelectWithJoins(metadata, depth, ignoredTypes, selectQuery);
        var aliasMapping = BuildAliasMapping(metadata, joinTables);
        var whereClause = BuildWhereClause(aliasMapping);
        var sql = baseSql.Replace("WHERE 1=1", string.IsNullOrEmpty(whereClause) ? "WHERE 1=1" : $"WHERE {whereClause}");

        var parameters = BuildParameterObject();
        var context = new SelectContext(connection, selectQuery?.CurrentTransaction, metadata, selectQuery)
        {
            Sql = sql,
            Parameters = parameters
        };
        await OrmMapper.RunBeforeSelectInterceptorsAsync(context, selectQuery?.Interceptors);
        var results = (await OrmMapper.ExecuteMultiMappingQuery<T>(connection, sql, parameters, splitMapping, metadata, selectQuery?.CurrentTransaction)).ToList();
        context.Result = results;
        await OrmMapper.RunAfterSelectInterceptorsAsync(context, selectQuery?.Interceptors);
        return results;
    }

    private Dictionary<string, string> BuildAliasMapping(ClassMetadata metadata, List<OrmMapper.JoinTableInfo> joinTables)
    {
        var aliasMapping = new Dictionary<string, string>
        {
            { "{BASE_TABLE}", $"\"{metadata.TableName}\"" }
        };

        foreach (var joinTable in joinTables)
        {
            var templateKey = $"{{FK_{joinTable.PropertyName.ToUpper()}}}";
            aliasMapping[templateKey] = $"\"{joinTable.Alias}\"";

            var tableTemplateKey = $"{{TABLE_{joinTable.JoinTable.ToUpper()}}}";
            if (!aliasMapping.ContainsKey(tableTemplateKey))
            {
                aliasMapping[tableTemplateKey] = $"\"{joinTable.Alias}\"";
            }
        }

        return aliasMapping;
    }

    private string BuildWhereClause(Dictionary<string, string> aliasMapping)
    {
        if (!_conditions.Any()) return string.Empty;

        var resolvedConditions = new List<string>();

        foreach (var condition in _conditions)
        {
            var resolvedSql = condition.Sql;
            foreach (var mapping in aliasMapping)
            {
                resolvedSql = resolvedSql.Replace(mapping.Key, mapping.Value);
            }
            resolvedConditions.Add($"({resolvedSql})");
        }

        return string.Join(" AND ", resolvedConditions);
    }

    private object BuildParameterObject()
    {
        var paramDict = new Dictionary<string, object>();
        foreach (var param in _parameters)
        {
            if (param is KeyValuePair<string, object> kvp)
            {
                paramDict[kvp.Key] = kvp.Value;
            }
        }
        return paramDict;
    }

    private static (string Sql, List<OrmMapper.SplitMapping> SplitMapping, List<OrmMapper.JoinTableInfo> JoinTables) BuildOptimizedSelectWithJoins(ClassMetadata metadata, int depth, HashSet<Type> ignoredTypes, SelectQuery? selectQuery)
    {
        var method = typeof(OrmMapper).GetMethod(nameof(BuildOptimizedSelectWithJoins), BindingFlags.NonPublic | BindingFlags.Static);
        var result = method.Invoke(null, new object[] { metadata, depth, ignoredTypes, selectQuery });

        var resultType = result.GetType();
        var sql = (string)resultType.GetField("Item1").GetValue(result);
        var splitMapping = (List<OrmMapper.SplitMapping>)resultType.GetField("Item2").GetValue(result);
        var joinTables = (List<OrmMapper.JoinTableInfo>)resultType.GetField("Item3").GetValue(result);

        return (sql, splitMapping, joinTables);
    }
}

// Expression visitor to convert LINQ expressions to SQL with templates
public class WhereExpressionVisitor : ExpressionVisitor
{
    private readonly StringBuilder _sql = new();
    private readonly List<object> _parameters;
    private readonly ClassMetadata _rootMetadata;
    public int ParameterIndex { get; private set; }
    public List<object> Parameters => _parameters;

    public WhereExpressionVisitor(List<object> parameters, int startIndex, ClassMetadata rootMetadata)
    {
        _parameters = parameters;
        ParameterIndex = startIndex;
        _rootMetadata = rootMetadata;
    }

    public string Visit(Expression expression)
    {
        base.Visit(expression);
        return _sql.ToString();
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        _sql.Append('(');

        // Check for Property vs Value comparison (handling Conversions)
        if (TryHandleConvertedComparison(node))
        {
            _sql.Append(')');
            return node;
        }

        bool leftIsNull = IsNullConstant(node.Left);
        bool rightIsNull = IsNullConstant(node.Right);

        if (rightIsNull || leftIsNull)
        {
            Expression nonNullSide = rightIsNull ? node.Left : node.Right;
            Visit(nonNullSide);

            switch (node.NodeType)
            {
                case ExpressionType.Equal:
                    _sql.Append(" IS NULL");
                    break;
                case ExpressionType.NotEqual:
                    _sql.Append(" IS NOT NULL");
                    break;
                default:
                    throw new NotSupportedException($"Operator {node.NodeType} is not supported with NULL");
            }

            _sql.Append(')');
            return node;
        }

        Visit(node.Left);

        string op = node.NodeType switch
        {
            ExpressionType.Equal => " = ",
            ExpressionType.NotEqual => " <> ",
            ExpressionType.GreaterThan => " > ",
            ExpressionType.GreaterThanOrEqual => " >= ",
            ExpressionType.LessThan => " < ",
            ExpressionType.LessThanOrEqual => " <= ",
            ExpressionType.AndAlso => " AND ",
            ExpressionType.OrElse => " OR ",
            _ => throw new NotSupportedException($"Binary operator {node.NodeType} is not supported")
        };

        _sql.Append(op);
        Visit(node.Right);
        _sql.Append(')');

        return node;
    }


    public static object ConvertType(Expression node, object value)
    {
        if (value == null || node == null)
            return value;

        // Check for unary (Convert, ConvertChecked)
        if (node is UnaryExpression unary)
        {
            Type operandType = unary.Operand.Type;
            Type targetType = Nullable.GetUnderlyingType(operandType) ?? operandType;

            if (targetType.IsEnum)
            {
                // string -> enum
                if (value is string s)
                    return Enum.Parse(targetType, s, ignoreCase: true);

                // numeric -> enum
                return Enum.ToObject(targetType, value);
            }
        }

        // Not unary or not enum -> return raw value
        return value;
    }

    private bool TryHandleConvertedComparison(BinaryExpression node)
    {
        if (node.NodeType != ExpressionType.Equal && node.NodeType != ExpressionType.NotEqual &&
            node.NodeType != ExpressionType.GreaterThan && node.NodeType != ExpressionType.GreaterThanOrEqual &&
            node.NodeType != ExpressionType.LessThan && node.NodeType != ExpressionType.LessThanOrEqual)
        {
            return false;
        }

        Expression propExpr = null;
        Expression valExpr = null;
        bool reversed = false;

        // Try to find if either side is a property access (unwrapping conversions)
        if (TryGetPropertyMetadata(node.Left, out var propMeta, out var template))
        {
            propExpr = node.Left;
            valExpr = node.Right;
        }
        else if (TryGetPropertyMetadata(node.Right, out propMeta, out template))
        {
            propExpr = node.Right;
            valExpr = node.Left;
            reversed = true;
        }

        if (propMeta != null)
        {
            var converter = propMeta.Converter;
            if (converter != null)
            {
                string op = node.NodeType switch
                {
                    ExpressionType.Equal => " = ",
                    ExpressionType.NotEqual => " <> ",
                    ExpressionType.GreaterThan => " > ",
                    ExpressionType.GreaterThanOrEqual => " >= ",
                    ExpressionType.LessThan => " < ",
                    ExpressionType.LessThanOrEqual => " <= ",
                    _ => " = "
                };

                // If the user wrote (1 == x.Status), we flip the op to keep SQL (Status = 1)
                if (reversed)
                {
                    op = FlipOperator(op);
                }

                _sql.Append(template);
                _sql.Append(op);

                object rawValue = GetExpressionValue(valExpr);
                object val = ConvertType(propExpr, rawValue);
                object convertedValue = converter.FromProvider(val);

                var paramName = $"@p{ParameterIndex++}";
                _parameters.Add(new KeyValuePair<string, object>(paramName.Substring(1), convertedValue));
                _sql.Append(paramName);

                return true;
            }
        }

        return false;
    }

    private string FlipOperator(string op)
    {
        return op.Trim() switch
        {
            "=" => " = ",
            "<>" => " <> ",
            ">" => " < ",
            ">=" => " <= ",
            "<" => " > ",
            "<=" => " >= ",
            _ => op
        };
    }

    private static bool IsNullConstant(Expression expr)
    {
        return expr is ConstantExpression c && c.Value == null;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (TryGetPropertyMetadata(node, out _, out var columnTemplate))
        {
            _sql.Append(columnTemplate);
        }
        else
        {
            var value = GetMemberValue(node);
            var paramName = $"@p{ParameterIndex++}";
            _parameters.Add(new KeyValuePair<string, object>(paramName.Substring(1), value));
            _sql.Append(paramName);
        }

        return node;
    }

    // UPDATED: Now accepts generic Expression and unwraps Converts
    private bool TryGetPropertyMetadata(Expression expr, out PropertyMetadata propertyMetadata, out string columnTemplate)
    {
        propertyMetadata = null;
        columnTemplate = "";

        // --- FIX: Unwrap casts/conversions (e.g. Convert(x.Status)) ---
        while (expr.NodeType == ExpressionType.Convert || expr.NodeType == ExpressionType.ConvertChecked)
        {
            expr = ((UnaryExpression)expr).Operand;
        }
        // -------------------------------------------------------------

        if (!(expr is MemberExpression node)) return false;

        var isNullableValueAccess =
            node.Member.Name == nameof(Nullable<int>.Value) &&
            node.Member.DeclaringType?.IsGenericType == true &&
            node.Member.DeclaringType.GetGenericTypeDefinition() == typeof(Nullable<>);

        var memberPath = new List<string>();
        var currentExpression = node;

        while (currentExpression != null)
        {
            memberPath.Insert(0, currentExpression.Member.Name);

            if (currentExpression.Expression?.NodeType == ExpressionType.Parameter)
            {
                break;
            }
            else if (currentExpression.Expression is MemberExpression parentMember)
            {
                currentExpression = parentMember;
            }
            else
            {
                return false;
            }
        }

        if (currentExpression?.Expression?.NodeType != ExpressionType.Parameter)
        {
            return false;
        }

        // Nullable<T>.Value should resolve to the same column as Nullable<T> itself.
        if (isNullableValueAccess && memberPath.Count > 1)
        {
            memberPath.RemoveAt(memberPath.Count - 1);
        }

        var currentMetadata = _rootMetadata;

        // 1. Direct Property
        if (memberPath.Count == 1)
        {
            var propName = memberPath[0];
            var prop = currentMetadata.Properties.FirstOrDefault(p => p.Property.Name == propName);

            if (prop != null)
            {
                propertyMetadata = prop;
                columnTemplate = $"{{BASE_TABLE}}.\"{prop.ColumnName}\"";
                return true;
            }
            columnTemplate = $"{{BASE_TABLE}}.\"{OrmMapper.GetDefaultColumnName(currentMetadata, propName)}\"";
            return true;
        }

        // 2. Nested/FK Property
        if (memberPath.Count >= 2)
        {
            var fkPropName = memberPath[0];
            var fkProp = OrmMapper.LoadAllForeignKeys(currentMetadata).FirstOrDefault(fk => fk.Property.Name == fkPropName);

            if (fkProp != null)
            {
                var targetMetadata = OrmMapper.GetMetadata(fkProp.ForeignKeyType);
                var fkTemplate = $"{{FK_{fkPropName.ToUpper()}}}";

                if (memberPath.Count == 2)
                {
                    var targetPropName = memberPath[1];
                    var targetProp = targetMetadata.Properties.FirstOrDefault(p => p.Property.Name == targetPropName);

                    if (targetProp != null)
                    {
                        propertyMetadata = targetProp;
                        columnTemplate = $"{fkTemplate}.\"{targetProp.ColumnName}\"";
                    }
                    else
                    {
                        columnTemplate = $"{fkTemplate}.\"{OrmMapper.GetDefaultColumnName(targetMetadata, targetPropName)}\"";
                    }
                    return true;
                }

                // Deep nesting logic (metadata retrieval becomes harder here, ignoring propMeta for now)
            }
        }

        return ResolveTemplateOnly(memberPath, currentMetadata, out columnTemplate);
    }

    private bool ResolveTemplateOnly(List<string> memberPath, ClassMetadata rootMetadata, out string columnTemplate)
    {
        columnTemplate = "";

        if (memberPath.Count == 1)
        {
            var p = rootMetadata.Properties.FirstOrDefault(x => x.Property.Name == memberPath[0]);
            columnTemplate = p != null ? $"{{BASE_TABLE}}.\"{p.ColumnName}\"" : $"{{BASE_TABLE}}.\"{OrmMapper.GetDefaultColumnName(rootMetadata, memberPath[0])}\"";
            return true;
        }

        var fkPropertyName = memberPath[0];
        var targetPropertyName = memberPath[1];

        var fkProperty = OrmMapper.LoadAllForeignKeys(rootMetadata).FirstOrDefault(fk => fk.Property.Name == fkPropertyName);

        if (fkProperty != null)
        {
            var fkTemplate = $"{{FK_{fkPropertyName.ToUpper()}}}";

            if (memberPath.Count == 2)
            {
                var targetMetadata = OrmMapper.GetMetadata(fkProperty.ForeignKeyType);
                var targetProperty = targetMetadata.Properties.FirstOrDefault(p => p.Property.Name == targetPropertyName);
                columnTemplate = targetProperty != null
                    ? $"{fkTemplate}.\"{targetProperty.ColumnName}\""
                    : $"{fkTemplate}.\"{OrmMapper.GetDefaultColumnName(targetMetadata, targetPropertyName)}\"";
                return true;
            }
            else
            {
                var secondFkProperty = memberPath[1];
                var nestedFkTemplate = $"{{FK_{fkPropertyName.ToUpper()}_{secondFkProperty.ToUpper()}}}";
                var finalProperty = memberPath[2];
                columnTemplate = $"{nestedFkTemplate}.\"{OrmMapper.GetDefaultColumnName(rootMetadata, finalProperty)}\"";
                return true;
            }
        }

        var tableTemplate = $"{{TABLE_{fkPropertyName.ToUpper()}}}";
        columnTemplate = $"{tableTemplate}.\"{OrmMapper.GetDefaultColumnName(rootMetadata, targetPropertyName)}\"";
        return true;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        var paramName = $"@p{ParameterIndex++}";
        _parameters.Add(new KeyValuePair<string, object>(paramName.Substring(1), node.Value));
        _sql.Append(paramName);
        return node;
    }

    //protected override Expression VisitMethodCall(MethodCallExpression node)
    //{
    //    if (node.Method.Name == "Contains")
    //    {
    //        if (node.Object != null && node.Object.Type == typeof(string))
    //        {
    //            Visit(node.Object);
    //            _sql.Append(" ILIKE ");

    //            var searchValue = GetExpressionValue(node.Arguments[0]);
    //            var paramName = $"@p{ParameterIndex++}";
    //            _parameters.Add(new KeyValuePair<string, object>(
    //                paramName.Substring(1),
    //                $"%{searchValue}%"
    //            ));
    //            _sql.Append(paramName);
    //        }
    //        else if (node.Object != null && typeof(IEnumerable).IsAssignableFrom(node.Object.Type))
    //        {
    //            var collection = GetExpressionValue(node.Object) as IEnumerable;
    //            if (collection == null)
    //                throw new InvalidOperationException("Contains collection could not be evaluated");

    //            var propertyExpr = node.Arguments[0];

    //            // Determine if property has converter
    //            PropertyMetadata propMeta = null;
    //            string colTemplate = null;
    //            TryGetPropertyMetadata(propertyExpr, out propMeta, out colTemplate);

    //            Visit(propertyExpr);

    //            var paramName = $"@p{ParameterIndex++}";
    //            var list = new List<object>();
    //            foreach (var item in collection) list.Add(item);

    //            if (propMeta != null)
    //            {
    //                var converter = propMeta.Converter;
    //                if (converter != null)
    //                {
    //                    for (int i = 0; i < list.Count; i++)
    //                    {
    //                        list[i] = converter.FromProvider(list[i]);
    //                    }
    //                }
    //            }

    //            _parameters.Add(new KeyValuePair<string, object>(paramName.Substring(1), list.ToArray()));
    //            _sql.Append($" = ANY({paramName})");
    //        }
    //        else
    //        {
    //            throw new NotSupportedException("Unsupported Contains usage");
    //        }
    //    }
    //    else
    //    {
    //        throw new NotSupportedException($"Method {node.Method.Name} is not supported");
    //    }

    //    return node;
    //}


    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.Name == "Contains")
        {
            // Case 1: string.Contains
            if (node.Object != null && node.Object.Type == typeof(string))
            {
                Visit(node.Object);
                _sql.Append(" ILIKE ");

                var searchValue = GetExpressionValue(node.Arguments[0]);
                var paramName = $"@p{ParameterIndex++}";
                _parameters.Add(new KeyValuePair<string, object>(
                    paramName.Substring(1),
                    $"%{searchValue}%"
                ));
                _sql.Append(paramName);
            }
            // Case 2: collection.Contains(value)
            else if (node.Object != null && typeof(IEnumerable).IsAssignableFrom(node.Object.Type))
            {
                var collection = GetExpressionValue(node.Object) as IEnumerable;
                if (collection == null)
                    throw new InvalidOperationException("Contains collection could not be evaluated");

                // The property being tested (e.g. pr.ProductId)
                var propertyExpr = node.Arguments[0];
                Visit(propertyExpr);

                // Build SQL "IN"
                var paramName = $"@p{ParameterIndex++}";

                // Cast explicitly depending on type
                object array;
                var elementType = node.Object.Type.IsGenericType
                    ? node.Object.Type.GetGenericArguments()[0]
                    : typeof(object);

                if (elementType == typeof(string))
                    array = collection.Cast<string>().ToArray();
                else if (elementType == typeof(int))
                    array = collection.Cast<int>().ToArray();
                else if (elementType == typeof(Guid))
                    array = collection.Cast<Guid>().ToArray();
                else
                    array = collection.Cast<object>().ToArray(); // fallback

                _parameters.Add(new KeyValuePair<string, object>(paramName.Substring(1), array));
                _sql.Append($" = ANY({paramName})");
            }
            else
            {
                throw new NotSupportedException("Unsupported Contains usage");
            }
        }
        else
        {
            throw new NotSupportedException($"Method {node.Method.Name} is not supported");
        }

        return node;
    }

    private object GetConverter(object propertyMetadata)
    {
        var propInfo = propertyMetadata.GetType().GetProperty("Converter");
        return propInfo?.GetValue(propertyMetadata);
    }

    private object ConvertValue(object converter, object value)
    {
        var methods = converter.GetType().GetMethods();
        var method = methods.FirstOrDefault(m => m.Name == "ToProvider") // Usually DB conversion
                     ?? methods.FirstOrDefault(m => m.Name == "FromProvider"); // Fallback per user request

        if (method != null)
        {
            return method.Invoke(converter, new object[] { value });
        }
        return value;
    }


    private object GetMemberValue(MemberExpression member)
    {
        var objectMember = Expression.Convert(member, typeof(object));
        var getterLambda = Expression.Lambda<Func<object>>(objectMember);
        var getter = getterLambda.Compile();
        return getter();
    }

    private object GetExpressionValue(Expression expression)
    {
        if (expression is ConstantExpression c)
            return c.Value;

        if (expression is MemberExpression m)
        {
            var lambda = Expression.Lambda<Func<object>>(Expression.Convert(m, typeof(object)));
            return lambda.Compile().Invoke();
        }

        if (expression is UnaryExpression u && u.NodeType == ExpressionType.Convert)
        {
            return GetExpressionValue(u.Operand);
        }

        var objectMember = Expression.Convert(expression, typeof(object));
        var getterLambda = Expression.Lambda<Func<object>>(objectMember);
        return getterLambda.Compile().Invoke();
    }
}

public class WhereCondition
{
    public string Sql { get; set; }
    public List<object> Parameters { get; set; }
}

public static class OrmMapperExtensions
{
    public static string ToSnakeCase(this string pascalCase)
    {
        if (string.IsNullOrEmpty(pascalCase)) return pascalCase;

        var result = new StringBuilder();
        result.Append(char.ToLower(pascalCase[0]));

        for (int i = 1; i < pascalCase.Length; i++)
        {
            if (char.IsUpper(pascalCase[i]))
            {
                result.Append('_');
                result.Append(char.ToLower(pascalCase[i]));
            }
            else
            {
                result.Append(pascalCase[i]);
            }
        }
        return result.ToString();
    }

    public static bool IsSimpleType(this Type type)
    {
        return type.IsPrimitive || type.IsEnum || type == typeof(string) ||
               type == typeof(DateTime) || type == typeof(DateOnly) || type == typeof(TimeOnly) ||
               type == typeof(DateTimeOffset) || type == typeof(decimal) || type == typeof(Guid) ||
               (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) &&
                IsSimpleType(type.GetGenericArguments()[0]));
    }
}
