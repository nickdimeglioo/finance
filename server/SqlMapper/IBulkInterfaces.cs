using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

public interface IBulkDialect
{
    /// <summary>
    /// Performs a high-performance bulk save using Temp Tables + Binary Import + Merge.
    /// Handles Inserts, Updates, and Upserts while returning generated IDs.
    /// </summary>
    Task BulkMergeAsync<T>(
        IDbConnection connection,
        IDbTransaction transaction,
        List<T> entities,   
        ClassMetadata metadata,
        BulkSaveOptions options
    ) where T : class;
}

// Configuration for Auditing
public class AuditConfig
{
    public bool Enabled { get; set; } = true;
    public string AuditTableName { get; set; } = "AuditLog";
    public string UserId { get; set; }
    public HashSet<string> AuditableProperties { get; set; } = new();
}


public class BulkSaveOptions
{
    public int ParameterLimit { get; set; } = 300;
    public bool UseTransaction { get; set; } = true;
    public SaveQuery SaveQuery { get; set; } = SaveQuery.Full();
    public IDbConnection? CurrentConnection { get; set; }
    public IDbTransaction? Transaction { get; set; }
    public HashSet<string> SpecificColumns { get; set; } = null; // null = all
    public IReadOnlyList<IDbInterceptor>? Interceptors { get; set; }
    public AuditConfig Audit { get; set; }
    public string AuditUserId { get; set; } = "System";
}
