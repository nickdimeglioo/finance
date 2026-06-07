using System.Text;
using FinanceTracker.Api.Features.Reports;
using FinanceTracker.Api.Features.Transactions;
using FinanceTracker.Api.Infrastructure.Storage;
using PipelineRunner.Services;

namespace FinanceTracker.Api.Services;

public sealed class ExportService
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;
    private readonly IObjectStorageService _storage;
    private readonly ReportService _reports;

    public ExportService(ICurrentUserContext currentUser, IOrmMapperService db, IObjectStorageService storage, ReportService reports)
    {
        _currentUser = currentUser;
        _db = db;
        _storage = storage;
        _reports = reports;
    }

    public async Task<ExportFileDto> CreateTransactionsExportAsync(ExportRequest request, CancellationToken cancellationToken)
    {
        var accountIds = new HashSet<Guid>(request.AccountIds ?? []);
        if (request.AccountId is Guid accountId)
        {
            accountIds.Add(accountId);
        }
        var transactions = (await _reports.LoadTransactionsAsync(request.From, request.To, cancellationToken))
            .Where(transaction => accountIds.Count == 0 || accountIds.Contains(transaction.AccountId))
            .Where(transaction => string.IsNullOrWhiteSpace(request.Classification) || transaction.Classification == request.Classification)
            .OrderBy(transaction => transaction.Date)
            .ThenBy(transaction => transaction.CreatedAt)
            .ToList();
        var csv = BuildTransactionCsv(transactions);
        var bytes = Encoding.UTF8.GetBytes(csv);
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var fileName = $"transactions-{now:yyyyMMdd-HHmmss}.csv";
        var key = $"exports/{_currentUser.UserId:D}/{id:D}/{fileName}";

        await using (var stream = new MemoryStream(bytes))
        {
            await _storage.PutAsync(key, stream, "text/csv", cancellationToken);
        }

        var entity = new ExportFile
        {
            Id = id,
            UserId = _currentUser.UserId,
            ExportType = "transactions",
            Filters = RulesetJson.Write(request),
            S3ObjectKey = key,
            ContentType = "text/csv",
            FileName = fileName,
            SizeBytes = bytes.Length,
            CreatedAt = now,
            ExpiresAt = now.AddDays(30)
        };
        await _db.SaveAsync(entity, auditUserId: _currentUser.UserId.ToString());
        return ToDto(entity);
    }

    public async Task<IReadOnlyList<ExportFileDto>> ListAsync(CancellationToken cancellationToken)
    {
        var exports = await _db.QuerySelect<ExportFile>()
            .From<ExportFile>()
            .SelectAllFrom<ExportFile>()
            .Where(exportFile => exportFile.UserId == _currentUser.UserId)
            .ToListAsync();
        return exports.OrderByDescending(exportFile => exportFile.CreatedAt).Select(ToDto).ToList();
    }

    public async Task<(ExportFile Entity, StoredObject Object)?> GetContentAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.GetByIdAsync<ExportFile>(id, depth: 0);
        if (entity?.UserId != _currentUser.UserId)
        {
            return null;
        }

        var stored = await _storage.GetAsync(entity.S3ObjectKey, cancellationToken);
        return (entity, stored);
    }

    public async Task<object?> GetDownloadUrlAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.GetByIdAsync<ExportFile>(id, depth: 0);
        return entity?.UserId == _currentUser.UserId
            ? new { downloadUrl = $"/api/v1/exports/{id:D}/content", expiresAt = entity.ExpiresAt }
            : null;
    }

    private static string BuildTransactionCsv(IReadOnlyList<FinancialTransaction> transactions)
    {
        var builder = new StringBuilder();
        builder.AppendLine("date,posted_at,description,merchant,type,classification,category,amount,currency,status,source");
        foreach (var transaction in transactions)
        {
            builder.AppendCsv(transaction.Date.ToString("yyyy-MM-dd")).Append(',');
            builder.AppendCsv(transaction.PostedAt?.ToString("yyyy-MM-dd") ?? string.Empty).Append(',');
            builder.AppendCsv(transaction.Description).Append(',');
            builder.AppendCsv(transaction.Merchant ?? string.Empty).Append(',');
            builder.AppendCsv(transaction.Type).Append(',');
            builder.AppendCsv(transaction.Classification).Append(',');
            builder.AppendCsv(transaction.Category ?? string.Empty).Append(',');
            builder.AppendCsv(transaction.Amount.ToString("0.00")).Append(',');
            builder.AppendCsv(transaction.Currency).Append(',');
            builder.AppendCsv(transaction.Status).Append(',');
            builder.AppendCsv(transaction.Source).AppendLine();
        }

        return builder.ToString();
    }

    private static ExportFileDto ToDto(ExportFile exportFile)
        => new(
            exportFile.Id,
            exportFile.ExportType,
            exportFile.FileName,
            exportFile.ContentType,
            exportFile.SizeBytes,
            exportFile.CreatedAt,
            exportFile.ExpiresAt,
            $"/api/v1/exports/{exportFile.Id:D}/content");
}

internal static class CsvStringBuilderExtensions
{
    public static StringBuilder AppendCsv(this StringBuilder builder, string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            builder.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
            return builder;
        }

        builder.Append(value);
        return builder;
    }
}
