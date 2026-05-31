namespace FinanceTracker.Api.Services;

public sealed class ImportService
{
    public string BuildRawImportPrefix(Guid userId, Guid importBatchId)
    {
        return $"imports/raw/{userId:D}/{importBatchId:D}";
    }
}

