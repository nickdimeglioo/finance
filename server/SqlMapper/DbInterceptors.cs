using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public interface IDbInterceptor
{
    Task BeforeInsert(InsertContext ctx, CancellationToken ct) => Task.CompletedTask;
    Task AfterInsert(InsertContext ctx, CancellationToken ct) => Task.CompletedTask;

    Task BeforeSelect(SelectContext ctx, CancellationToken ct) => Task.CompletedTask;
    Task AfterSelect(SelectContext ctx, CancellationToken ct) => Task.CompletedTask;

    Task BeforeUpdate(UpdateContext ctx, CancellationToken ct) => Task.CompletedTask;
    Task AfterUpdate(UpdateContext ctx, CancellationToken ct) => Task.CompletedTask;

    Task BeforeDelete(DeleteContext ctx, CancellationToken ct) => Task.CompletedTask;
    Task AfterDelete(DeleteContext ctx, CancellationToken ct) => Task.CompletedTask;
}

public abstract class DbOperationContext
{
    protected DbOperationContext(
        IDbConnection connection,
        IDbTransaction? transaction,
        ClassMetadata metadata,
        IEnumerable<object>? entities = null,
        Dictionary<object, object>? items = null)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Transaction = transaction;
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        Entities = entities?.ToList() ?? [];
        Items = items ?? new Dictionary<object, object>();
    }

    public IDbConnection Connection { get; }
    public IDbTransaction? Transaction { get; }
    public ClassMetadata Metadata { get; }
    public Type EntityType => Metadata.Type;
    public IReadOnlyList<object> Entities { get; }
    public Dictionary<object, object> Items { get; }
}

public sealed class InsertContext : DbOperationContext
{
    public InsertContext(
        IDbConnection connection,
        IDbTransaction? transaction,
        ClassMetadata metadata,
        IEnumerable<object> entities,
        Dictionary<object, object>? items = null)
        : base(connection, transaction, metadata, entities, items)
    {
    }
}

public sealed class SelectContext : DbOperationContext
{
    public SelectContext(
        IDbConnection connection,
        IDbTransaction? transaction,
        ClassMetadata metadata,
        SelectQuery? selectQuery,
        Dictionary<object, object>? items = null)
        : base(connection, transaction, metadata, null, items)
    {
        SelectQuery = selectQuery;
    }

    public SelectQuery? SelectQuery { get; }
    public string? Sql { get; set; }
    public object? Parameters { get; set; }
    public object? Result { get; set; }
}

public sealed class UpdateContext : DbOperationContext
{
    public UpdateContext(
        IDbConnection connection,
        IDbTransaction? transaction,
        ClassMetadata metadata,
        IEnumerable<object> entities,
        Dictionary<object, object>? items = null)
        : base(connection, transaction, metadata, entities, items)
    {
    }
}

public sealed class DeleteContext : DbOperationContext
{
    public DeleteContext(
        IDbConnection connection,
        IDbTransaction? transaction,
        ClassMetadata metadata,
        IEnumerable<object> entities,
        DeleteType deleteType,
        bool isSoftDelete,
        Dictionary<object, object>? items = null)
        : base(connection, transaction, metadata, entities, items)
    {
        DeleteType = deleteType;
        IsSoftDelete = isSoftDelete;
    }

    public DeleteType DeleteType { get; }
    public bool IsSoftDelete { get; }
}

public static partial class OrmMapper
{
    private static readonly object InterceptorsLock = new();
    private static IReadOnlyList<IDbInterceptor> GlobalInterceptors { get; set; } = [];

    public static void SetInterceptors(IEnumerable<IDbInterceptor>? interceptors)
    {
        lock (InterceptorsLock)
        {
            GlobalInterceptors = interceptors?.ToList() ?? [];
        }
    }

    internal static IReadOnlyList<IDbInterceptor> ResolveInterceptors(IEnumerable<IDbInterceptor>? scopedInterceptors)
    {
        if (scopedInterceptors != null)
        {
            return scopedInterceptors as IReadOnlyList<IDbInterceptor> ?? scopedInterceptors.ToList();
        }

        lock (InterceptorsLock)
        {
            return GlobalInterceptors.ToList();
        }
    }

    internal static async Task RunBeforeInsertInterceptorsAsync(InsertContext ctx, IEnumerable<IDbInterceptor>? interceptors, CancellationToken ct = default)
    {
        foreach (var interceptor in ResolveInterceptors(interceptors))
        {
            await interceptor.BeforeInsert(ctx, ct);
        }
    }

    internal static async Task RunAfterInsertInterceptorsAsync(InsertContext ctx, IEnumerable<IDbInterceptor>? interceptors, CancellationToken ct = default)
    {
        foreach (var interceptor in ResolveInterceptors(interceptors))
        {
            await interceptor.AfterInsert(ctx, ct);
        }
    }

    internal static async Task RunBeforeSelectInterceptorsAsync(SelectContext ctx, IEnumerable<IDbInterceptor>? interceptors, CancellationToken ct = default)
    {
        foreach (var interceptor in ResolveInterceptors(interceptors))
        {
            await interceptor.BeforeSelect(ctx, ct);
        }
    }

    internal static async Task RunAfterSelectInterceptorsAsync(SelectContext ctx, IEnumerable<IDbInterceptor>? interceptors, CancellationToken ct = default)
    {
        foreach (var interceptor in ResolveInterceptors(interceptors))
        {
            await interceptor.AfterSelect(ctx, ct);
        }
    }

    internal static async Task RunBeforeUpdateInterceptorsAsync(UpdateContext ctx, IEnumerable<IDbInterceptor>? interceptors, CancellationToken ct = default)
    {
        foreach (var interceptor in ResolveInterceptors(interceptors))
        {
            await interceptor.BeforeUpdate(ctx, ct);
        }
    }

    internal static async Task RunAfterUpdateInterceptorsAsync(UpdateContext ctx, IEnumerable<IDbInterceptor>? interceptors, CancellationToken ct = default)
    {
        foreach (var interceptor in ResolveInterceptors(interceptors))
        {
            await interceptor.AfterUpdate(ctx, ct);
        }
    }

    internal static async Task RunBeforeDeleteInterceptorsAsync(DeleteContext ctx, IEnumerable<IDbInterceptor>? interceptors, CancellationToken ct = default)
    {
        foreach (var interceptor in ResolveInterceptors(interceptors))
        {
            await interceptor.BeforeDelete(ctx, ct);
        }
    }

    internal static async Task RunAfterDeleteInterceptorsAsync(DeleteContext ctx, IEnumerable<IDbInterceptor>? interceptors, CancellationToken ct = default)
    {
        foreach (var interceptor in ResolveInterceptors(interceptors))
        {
            await interceptor.AfterDelete(ctx, ct);
        }
    }
}
