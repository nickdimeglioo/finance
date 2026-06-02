using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

public static partial class OrmMapper
{
    // Entry point for projection-style SQL queries with joins.
    public static OrmSelectQueryable<T> QuerySelect<T>(SelectQuery? selectQuery = null) where T : class, new()
    {
        return new OrmSelectQueryable<T>(selectQuery);
    }
}

public class OrmSelectQueryable<T> where T : class, new()
{
    private readonly List<string> _selectClauses = new();
    private readonly List<JoinClause> _joins = new();
    private readonly List<string> _whereClauses = new();
    private readonly List<string> _orderByClauses = new();
    private readonly Dictionary<string, object?> _parameters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, QuerySource> _sourcesByAlias = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, List<QuerySource>> _sourcesByType = new();
    private readonly SelectQuery? _selectQuery;

    private string? _fromTable;
    private string? _fromAlias;
    private bool _distinct;
    private int? _limit;
    private int? _offset;
    private int _parameterIndex;
    private int _aliasIndex;

    public OrmSelectQueryable(SelectQuery? selectQuery = null)
    {
        _selectQuery = selectQuery;
    }

    public OrmSelectQueryable<T> From<TSource>(string? alias = null, string? tableNameOverride = null) where TSource : class
    {
        var metadata = OrmMapper.GetMetadata<TSource>();
        var resolvedAlias = string.IsNullOrWhiteSpace(alias) ? NextAlias() : alias!;
        var tableName = tableNameOverride ?? metadata.TableName;
        return FromInternal(tableName, resolvedAlias, typeof(TSource), metadata);
    }

    public OrmSelectQueryable<T> From(string tableName, string alias)
    {
        return FromInternal(tableName, alias, null, null);
    }

    private OrmSelectQueryable<T> FromInternal(string tableName, string alias, Type? sourceType, ClassMetadata? metadata)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));
        if (string.IsNullOrWhiteSpace(alias))
            throw new ArgumentException("Alias is required.", nameof(alias));
        if (!string.IsNullOrWhiteSpace(_fromTable))
            throw new InvalidOperationException("From(...) is already configured for this query.");

        _fromTable = tableName;
        _fromAlias = alias;
        RegisterSource(alias, tableName, sourceType, metadata);
        return this;
    }

    public OrmSelectQueryable<T> Distinct()
    {
        _distinct = true;
        return this;
    }

    public OrmSelectQueryable<T> Select(string expression, string? alias = null)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("Select expression is required.", nameof(expression));

        _selectClauses.Add(string.IsNullOrWhiteSpace(alias)
            ? expression
            : $"{expression} AS {QuoteIdentifier(alias)}");

        return this;
    }

    public OrmSelectQueryable<T> SelectAllFrom<TSource>(string? alias = null) where TSource : class
    {
        var source = ResolveSource(typeof(TSource), alias);
        var metadata = source.Metadata ?? OrmMapper.GetMetadata<TSource>();
        var simpleColumns = metadata.LoadableProperties
            .Where(x => !x.IsForeignKey && OrmMapper.IsSimpleType(x.Property.PropertyType));

        foreach (var prop in simpleColumns)
        {
            _selectClauses.Add(
                $"{QuoteIdentifier(source.Alias)}.{QuoteIdentifier(prop.ColumnName)} AS {QuoteIdentifier(prop.Property.Name)}");
        }

        return this;
    }

    public OrmSelectQueryable<T> Select<TSource>(
        Expression<Func<TSource, object?>> sourceSelector,
        Expression<Func<T, object?>> targetSelector,
        string? alias = null) where TSource : class
    {
        var source = ResolveSource(typeof(TSource), alias);
        var sourcePropertyName = ExtractDirectPropertyName(sourceSelector.Body);
        var targetPropertyName = ExtractDirectPropertyName(targetSelector.Body);
        var column = BuildColumnReference(source, sourcePropertyName);

        _selectClauses.Add($"{column} AS {QuoteIdentifier(targetPropertyName)}");
        return this;
    }

    public OrmSelectQueryable<T> Select<TSource>(Expression<Func<TSource, T>> projection, string? alias = null) where TSource : class
    {
        if (projection.Body is not MemberInitExpression memberInit)
            throw new NotSupportedException("Projection select must be a member initializer like: x => new TResult { ... }");

        var source = ResolveSource(typeof(TSource), alias);
        foreach (var binding in memberInit.Bindings.OfType<MemberAssignment>())
        {
            var sourcePropertyName = ExtractDirectPropertyName(binding.Expression);
            var column = BuildColumnReference(source, sourcePropertyName);
            _selectClauses.Add($"{column} AS {QuoteIdentifier(binding.Member.Name)}");
        }

        return this;
    }

    public OrmSelectQueryable<T> Join<TJoin>(string alias, string onCondition, string? tableNameOverride = null) where TJoin : class
    {
        var metadata = OrmMapper.GetMetadata<TJoin>();
        var tableName = tableNameOverride ?? metadata.TableName;
        return AddJoin("INNER", tableName, alias, onCondition, typeof(TJoin), metadata);
    }

    public OrmSelectQueryable<T> Join(string tableName, string alias, string onCondition)
    {
        return AddJoin("INNER", tableName, alias, onCondition);
    }

    public OrmSelectQueryable<T> Join<TJoin, TLeft>(
        Expression<Func<TLeft, object?>> leftKeySelector,
        Expression<Func<TJoin, object?>> rightKeySelector,
        string? alias = null,
        string? leftAlias = null,
        string? tableNameOverride = null)
        where TJoin : class
        where TLeft : class
    {
        return AddTypedJoin("INNER", leftKeySelector, rightKeySelector, alias, leftAlias, tableNameOverride);
    }

    public OrmSelectQueryable<T> LeftJoin<TJoin>(string alias, string onCondition, string? tableNameOverride = null) where TJoin : class
    {
        var metadata = OrmMapper.GetMetadata<TJoin>();
        var tableName = tableNameOverride ?? metadata.TableName;
        return AddJoin("LEFT", tableName, alias, onCondition, typeof(TJoin), metadata);
    }

    public OrmSelectQueryable<T> LeftJoin(string tableName, string alias, string onCondition)
    {
        return AddJoin("LEFT", tableName, alias, onCondition);
    }

    public OrmSelectQueryable<T> LeftJoin<TJoin, TLeft>(
        Expression<Func<TLeft, object?>> leftKeySelector,
        Expression<Func<TJoin, object?>> rightKeySelector,
        string? alias = null,
        string? leftAlias = null,
        string? tableNameOverride = null)
        where TJoin : class
        where TLeft : class
    {
        return AddTypedJoin("LEFT", leftKeySelector, rightKeySelector, alias, leftAlias, tableNameOverride);
    }

    public OrmSelectQueryable<T> Where(string sql, object? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new ArgumentException("Where SQL is required.", nameof(sql));

        var sqlWithBoundParameters = BindParameters(sql, parameters);
        _whereClauses.Add(sqlWithBoundParameters);
        return this;
    }

    public OrmSelectQueryable<T> Where(Expression<Func<T, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));
        if (string.IsNullOrWhiteSpace(_fromAlias))
            throw new InvalidOperationException("From(...) must be called before expression-based Where.");

        var metadata = OrmMapper.GetMetadata<T>();
        var sql = BuildWhereFromExpression(predicate.Body, metadata, _fromAlias);
        _whereClauses.Add(sql);
        return this;
    }

    public OrmSelectQueryable<T> Where<TSource>(Expression<Func<TSource, bool>> predicate, string? alias = null) where TSource : class
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        var source = ResolveSource(typeof(TSource), alias);
        var metadata = source.Metadata ?? OrmMapper.GetMetadata<TSource>();
        var sql = BuildWhereFromExpression(predicate.Body, metadata, source.Alias);
        _whereClauses.Add(sql);
        return this;
    }

    public OrmSelectQueryable<T> OrderBy(string expression, bool descending = false)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("Order by expression is required.", nameof(expression));

        _orderByClauses.Add(descending ? $"{expression} DESC" : $"{expression} ASC");
        return this;
    }

    public OrmSelectQueryable<T> OrderBy<TSource>(
        Expression<Func<TSource, object?>> selector,
        bool descending = false,
        string? alias = null) where TSource : class
    {
        if (selector == null)
            throw new ArgumentNullException(nameof(selector));

        var source = ResolveSource(typeof(TSource), alias);
        var propertyName = ExtractDirectPropertyName(selector.Body);
        var column = BuildColumnReference(source, propertyName);
        _orderByClauses.Add(descending ? $"{column} DESC" : $"{column} ASC");
        return this;
    }

    public OrmSelectQueryable<T> Take(int limit)
    {
        if (limit < 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit cannot be negative.");

        _limit = limit;
        return this;
    }

    public OrmSelectQueryable<T> Limit(int limit)
    {
        return Take(limit);
    }

    public OrmSelectQueryable<T> Skip(int offset)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative.");

        _offset = offset;
        return this;
    }

    public async Task<List<T>> ToListAsync(SelectQuery? selectQuery = null)
    {
        var effectiveSelectQuery = selectQuery ?? _selectQuery;
        var sql = BuildSelectSql(effectiveSelectQuery);
        var classMetadata = OrmMapper.GetMetadata<T>();
        return await ExecuteWithConnectionAsync(async (connection, transaction) =>
        {
            var context = new SelectContext(connection, transaction, classMetadata, effectiveSelectQuery)
            {
                Sql = sql,
                Parameters = _parameters
            };
            var cancellationToken = effectiveSelectQuery?.CancellationToken ?? default;
            await OrmMapper.RunBeforeSelectInterceptorsAsync(context, effectiveSelectQuery?.Interceptors, cancellationToken);
            var rows = (await OrmMapper.ExecuteMultiMappingQuery<T>(connection, sql, _parameters, [], classMetadata, transaction, cancellationToken)).ToList();
            context.Result = rows;
            await OrmMapper.RunAfterSelectInterceptorsAsync(context, effectiveSelectQuery?.Interceptors, cancellationToken);
            //var rows = await connection.QueryAsync<T>(sql, _parameters, transaction);
            return rows;
        }, effectiveSelectQuery);
    }

    public async Task<T?> FirstOrDefaultAsync(SelectQuery? selectQuery = null)
    {
        var effectiveSelectQuery = selectQuery ?? _selectQuery;
        var sql = BuildSelectSql(
            effectiveSelectQuery,
            forcedLimit: _limit.HasValue ? null : 1);

        var classMetadata = OrmMapper.GetMetadata<T>();

        return await ExecuteWithConnectionAsync( async
            (connection, transaction) => 
            {
                var context = new SelectContext(connection, transaction, classMetadata, effectiveSelectQuery)
                {
                    Sql = sql,
                    Parameters = _parameters
                };
                var cancellationToken = effectiveSelectQuery?.CancellationToken ?? default;
                await OrmMapper.RunBeforeSelectInterceptorsAsync(context, effectiveSelectQuery?.Interceptors, cancellationToken);
                var rows = (await OrmMapper.ExecuteMultiMappingQuery<T>(connection, sql, _parameters, [], classMetadata, transaction, cancellationToken)).ToList();
                context.Result = rows;
                await OrmMapper.RunAfterSelectInterceptorsAsync(context, effectiveSelectQuery?.Interceptors, cancellationToken);
                //connection.QueryFirstOrDefaultAsync<T>(sql, _parameters, transaction)
                return rows.FirstOrDefault();
             },
            effectiveSelectQuery);
    }

    public async Task<bool> AnyAsync(SelectQuery? selectQuery = null)
    {
        var effectiveSelectQuery = selectQuery ?? _selectQuery;
        var sql = BuildAnySql(effectiveSelectQuery);

        var classMetadata = OrmMapper.GetMetadata<T>();
        return await ExecuteWithConnectionAsync(async (connection, transaction) =>
        {
            var context = new SelectContext(connection, transaction, classMetadata, effectiveSelectQuery)
            {
                Sql = sql,
                Parameters = _parameters
            };
            var cancellationToken = effectiveSelectQuery?.CancellationToken ?? default;
            await OrmMapper.RunBeforeSelectInterceptorsAsync(context, effectiveSelectQuery?.Interceptors, cancellationToken);
            var result = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(sql, _parameters, transaction, cancellationToken: cancellationToken));
            context.Result = result;
            await OrmMapper.RunAfterSelectInterceptorsAsync(context, effectiveSelectQuery?.Interceptors, cancellationToken);
            return result;
        }, effectiveSelectQuery);
    }

    public string ToSql(SelectQuery? selectQuery = null)
    {
        var effectiveSelectQuery = selectQuery ?? _selectQuery;
        return BuildSelectSql(effectiveSelectQuery);
    }

    private OrmSelectQueryable<T> AddTypedJoin<TJoin, TLeft>(
        string joinType,
        Expression<Func<TLeft, object?>> leftKeySelector,
        Expression<Func<TJoin, object?>> rightKeySelector,
        string? alias,
        string? leftAlias,
        string? tableNameOverride)
        where TJoin : class
        where TLeft : class
    {
        var leftSource = ResolveSource(typeof(TLeft), leftAlias);
        var joinMetadata = OrmMapper.GetMetadata<TJoin>();
        var rightAlias = string.IsNullOrWhiteSpace(alias) ? NextAlias() : alias!;
        var rightTable = tableNameOverride ?? joinMetadata.TableName;

        var leftPropertyName = ExtractDirectPropertyName(leftKeySelector.Body);
        var rightPropertyName = ExtractDirectPropertyName(rightKeySelector.Body);
        var onCondition = $"{BuildColumnReference(leftSource, leftPropertyName)} = {BuildColumnReference(rightAlias, joinMetadata, rightPropertyName)}";

        return AddJoin(joinType, rightTable, rightAlias, onCondition, typeof(TJoin), joinMetadata);
    }

    private OrmSelectQueryable<T> AddJoin(
        string joinType,
        string tableName,
        string alias,
        string onCondition,
        Type? sourceType = null,
        ClassMetadata? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));
        if (string.IsNullOrWhiteSpace(alias))
            throw new ArgumentException("Alias is required.", nameof(alias));
        if (string.IsNullOrWhiteSpace(onCondition))
            throw new ArgumentException("Join condition is required.", nameof(onCondition));
        if (string.IsNullOrWhiteSpace(_fromAlias))
            throw new InvalidOperationException("From(...) must be called before Join(...).");
        if (_sourcesByAlias.ContainsKey(alias))
            throw new InvalidOperationException($"Alias '{alias}' is already used in this query.");

        _joins.Add(new JoinClause
        {
            JoinType = joinType,
            TableName = tableName,
            Alias = alias,
            OnCondition = onCondition
        });

        RegisterSource(alias, tableName, sourceType, metadata);
        return this;
    }

    private async Task<TResult> ExecuteWithConnectionAsync<TResult>(
        Func<IDbConnection, IDbTransaction?, Task<TResult>> action,
        SelectQuery? selectQuery)
    {
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
                    var result = await action(connection, transaction);
                    transaction.Commit();
                    return result;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
                finally
                {
                    selectQuery.CurrentTransaction = null;
                }
            }

            return await action(connection, selectQuery?.CurrentTransaction);
        }
        finally
        {
            if (!externalConnection)
                connection.Dispose();
        }
    }

    private string BuildAnySql(SelectQuery? selectQuery)
    {
        var innerSql = BuildSelectSql(
            selectQuery: selectQuery,
            explicitSelectClause: "1",
            includeOrderBy: false,
            includeLock: false,
            forcedLimit: _limit ?? 1,
            includeDistinct: false);

        return $"SELECT EXISTS ({innerSql})";
    }

    private string BuildWhereFromExpression(Expression predicate, ClassMetadata metadata, string alias)
    {
        var expressionParameters = new List<object>();
        var visitor = new WhereExpressionVisitor(expressionParameters, 0, metadata);
        var sql = visitor.Visit(predicate).Replace("{BASE_TABLE}", QuoteIdentifier(alias));
        return BindExpressionParameters(sql, expressionParameters);
    }

    private string BindExpressionParameters(string sql, List<object> expressionParameters)
    {
        foreach (var param in expressionParameters)
        {
            if (param is KeyValuePair<string, object> kvp)
            {
                var uniqueName = $"{kvp.Key}_{_parameterIndex++}";
                _parameters[uniqueName] = kvp.Value;
                sql = Regex.Replace(sql, $@"@{Regex.Escape(kvp.Key)}\b", $"@{uniqueName}");
            }
        }

        return sql;
    }

    private string BuildSelectSql(
        SelectQuery? selectQuery,
        string? explicitSelectClause = null,
        bool includeOrderBy = true,
        bool includeLock = true,
        int? forcedLimit = null,
        bool includeDistinct = true)
    {
        EnsureFromConfigured();

        var selectClause = explicitSelectClause;
        if (string.IsNullOrWhiteSpace(selectClause))
        {
            selectClause = _selectClauses.Count == 0 ? "*" : string.Join(", ", _selectClauses);
        }

        var sql = new StringBuilder();
        sql.Append("SELECT ");
        if (includeDistinct && _distinct)
            sql.Append("DISTINCT ");
        sql.Append(selectClause);
        sql.Append(" FROM ");
        sql.Append(QuoteIdentifier(_fromTable!));
        sql.Append(" ");
        sql.Append(QuoteIdentifier(_fromAlias!));

        foreach (var join in _joins)
        {
            sql.Append(" ");
            sql.Append(join.JoinType);
            sql.Append(" JOIN ");
            sql.Append(QuoteIdentifier(join.TableName));
            sql.Append(" ");
            sql.Append(QuoteIdentifier(join.Alias));
            sql.Append(" ON ");
            sql.Append(join.OnCondition);
        }

        var whereClauses = BuildSoftDeleteWhereClauses(selectQuery).Concat(_whereClauses).ToList();
        if (whereClauses.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.Append(string.Join(" AND ", whereClauses.Select(static x => $"({x})")));
        }

        if (includeOrderBy && _orderByClauses.Count > 0)
        {
            sql.Append(" ORDER BY ");
            sql.Append(string.Join(", ", _orderByClauses));
        }

        var finalLimit = forcedLimit ?? _limit;
        if (finalLimit.HasValue)
        {
            sql.Append(" LIMIT ");
            sql.Append(finalLimit.Value);
        }

        if (_offset.HasValue)
        {
            sql.Append(" OFFSET ");
            sql.Append(_offset.Value);
        }

        if (includeLock && selectQuery?.LockRow == true)
        {
            sql.Append(" FOR UPDATE");
            if (selectQuery.NoWait)
            {
                sql.Append(" NOWAIT");
            }
        }

        return sql.ToString();
    }

    private string BindParameters(string sql, object? parameters)
    {
        if (parameters == null)
            return sql;

        foreach (var pair in ExtractParameters(parameters))
        {
            var uniqueName = $"{pair.Key}_{_parameterIndex++}";
            _parameters[uniqueName] = pair.Value;
            sql = Regex.Replace(sql, $@"@{Regex.Escape(pair.Key)}\b", $"@{uniqueName}");
        }

        return sql;
    }

    private IEnumerable<string> BuildSoftDeleteWhereClauses(SelectQuery? selectQuery)
    {
        if (string.IsNullOrWhiteSpace(_fromAlias) ||
            !_sourcesByAlias.TryGetValue(_fromAlias, out var source) ||
            source.Metadata == null ||
            !OrmMapper.ShouldFilterSoftDeleted(source.Metadata, selectQuery))
        {
            yield break;
        }

        yield return OrmMapper.BuildSoftDeletePredicate(QuoteIdentifier(source.Alias), source.Metadata);
    }

    private void RegisterSource(string alias, string tableName, Type? sourceType, ClassMetadata? metadata)
    {
        if (_sourcesByAlias.ContainsKey(alias))
            throw new InvalidOperationException($"Alias '{alias}' is already used in this query.");

        var source = new QuerySource
        {
            Alias = alias,
            TableName = tableName,
            SourceType = sourceType,
            Metadata = metadata
        };

        _sourcesByAlias[source.Alias] = source;

        if (sourceType == null)
            return;

        if (!_sourcesByType.TryGetValue(sourceType, out var list))
        {
            list = new List<QuerySource>();
            _sourcesByType[sourceType] = list;
        }

        list.Add(source);
    }

    private QuerySource ResolveSource(Type sourceType, string? explicitAlias)
    {
        if (!string.IsNullOrWhiteSpace(explicitAlias))
        {
            if (!_sourcesByAlias.TryGetValue(explicitAlias!, out var aliasedSource))
                throw new InvalidOperationException($"Alias '{explicitAlias}' is not defined in this query.");
            if (aliasedSource.SourceType != null && aliasedSource.SourceType != sourceType)
                throw new InvalidOperationException($"Alias '{explicitAlias}' is not a source for type '{sourceType.Name}'.");
            return aliasedSource;
        }

        if (!_sourcesByType.TryGetValue(sourceType, out var typedSources) || typedSources.Count == 0)
            throw new InvalidOperationException($"No source registered for type '{sourceType.Name}'. Use From<{sourceType.Name}>() or provide an alias.");
        if (typedSources.Count > 1)
            throw new InvalidOperationException($"Multiple sources of type '{sourceType.Name}' exist. Pass an alias explicitly.");

        return typedSources[0];
    }

    private string NextAlias()
    {
        string alias;
        do
        {
            alias = $"t{_aliasIndex++}";
        } while (_sourcesByAlias.ContainsKey(alias));

        return alias;
    }

    private static string ExtractDirectPropertyName(Expression expression)
    {
        while (expression.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
        {
            expression = ((UnaryExpression)expression).Operand;
        }

        if (expression is not MemberExpression memberExpression ||
            memberExpression.Expression is not ParameterExpression)
        {
            throw new NotSupportedException("Only direct property access is supported in strongly-typed selectors.");
        }

        return memberExpression.Member.Name;
    }

    private string BuildColumnReference(QuerySource source, string propertyName)
    {
        var columnName = ResolveColumnName(source.Metadata, propertyName);
        return $"{QuoteIdentifier(source.Alias)}.{QuoteIdentifier(columnName)}";
    }

    private string BuildColumnReference(string alias, ClassMetadata metadata, string propertyName)
    {
        var columnName = ResolveColumnName(metadata, propertyName);
        return $"{QuoteIdentifier(alias)}.{QuoteIdentifier(columnName)}";
    }

    private static string ResolveColumnName(ClassMetadata? metadata, string propertyName)
    {
        if (metadata != null)
        {
            var property = metadata.Properties.FirstOrDefault(p => p.Property.Name == propertyName);
            if (property != null)
                return property.ColumnName;
        }

        return metadata == null
            ? OrmMapper.ApplyNamingConvention(propertyName, OrmMapper.Options.ColumnStyle)
            : OrmMapper.GetDefaultColumnName(metadata, propertyName);
    }

    private static IEnumerable<KeyValuePair<string, object?>> ExtractParameters(object parameters)
    {
        if (parameters is DynamicParameters dynamicParameters)
        {
            foreach (var name in dynamicParameters.ParameterNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                yield return new KeyValuePair<string, object?>(name, dynamicParameters.Get<object?>(name));
            }
            yield break;
        }

        if (parameters is IEnumerable<KeyValuePair<string, object?>> nullablePairs)
        {
            foreach (var pair in nullablePairs)
                yield return pair;
            yield break;
        }

        if (parameters is IEnumerable<KeyValuePair<string, object>> pairs)
        {
            foreach (var pair in pairs)
                yield return new KeyValuePair<string, object?>(pair.Key, pair.Value);
            yield break;
        }

        foreach (var prop in parameters.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            yield return new KeyValuePair<string, object?>(prop.Name, prop.GetValue(parameters));
        }
    }

    private void EnsureFromConfigured()
    {
        if (string.IsNullOrWhiteSpace(_fromTable) || string.IsNullOrWhiteSpace(_fromAlias))
        {
            throw new InvalidOperationException("From(...) must be configured before executing a query.");
        }
    }

    private static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Identifier is required.", nameof(identifier));

        if (identifier.StartsWith("\"", StringComparison.Ordinal) &&
            identifier.EndsWith("\"", StringComparison.Ordinal))
        {
            return identifier;
        }

        if (identifier.Contains('.', StringComparison.Ordinal))
        {
            var parts = identifier.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return string.Join(".", parts.Select(QuoteIdentifier));
        }

        return $"\"{identifier}\"";
    }

    private sealed class JoinClause
    {
        public string JoinType { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string Alias { get; set; } = string.Empty;
        public string OnCondition { get; set; } = string.Empty;
    }

    private sealed class QuerySource
    {
        public string Alias { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public Type? SourceType { get; set; }
        public ClassMetadata? Metadata { get; set; }
    }
}
