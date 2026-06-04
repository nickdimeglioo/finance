namespace FinanceTracker.Api.Configuration;

public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    public string Provider { get; set; } = "Local";
    public string ServiceUrl { get; set; } = "http://localhost:9000";
    public string Region { get; set; } = "us-east-1";
    public string BucketName { get; set; } = "finance-tracker";
    public string AccessKey { get; set; } = "pipeline";
    public string SecretKey { get; set; } = "pipeline-minio-password";
    public string LocalRootPath { get; set; } = "storage";
    public bool ForcePathStyle { get; set; } = true;
    public bool AutoCreateBucket { get; set; } = true;
    public bool SkipNetworkCheck { get; set; } = true;
    public StoragePrefixes Prefixes { get; set; } = new();
}

public sealed class StoragePrefixes
{
    public string RawImports { get; set; } = "imports/raw";
    public string ParsedImports { get; set; } = "imports/parsed";
    public string Exports { get; set; } = "exports";
    public string Attachments { get; set; } = "attachments";
}
