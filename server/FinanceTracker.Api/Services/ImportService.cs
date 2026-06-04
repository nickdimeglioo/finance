using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinanceTracker.Api.Features.Accounts;
using FinanceTracker.Api.Features.Imports;
using FinanceTracker.Api.Features.Rules;
using FinanceTracker.Api.Features.Shared;
using FinanceTracker.Api.Features.Transactions;
using FinanceTracker.Api.Infrastructure.Storage;
using PipelineRunner.Services;
using PipelineRunner.Utils;

namespace FinanceTracker.Api.Services;

public sealed class ImportService
{
    private const long MaxImportBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedContentTypes =
    [
        "text/csv",
        "application/csv",
        "application/vnd.ms-excel",
        "text/plain"
    ];

    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;
    private readonly IObjectStorageService _storage;
    private readonly ImportRuleService _importRules;
    private readonly ClassificationRuleService _classificationRules;
    private readonly StorageFileService _storageFiles;

    public ImportService(
        ICurrentUserContext currentUser,
        IOrmMapperService db,
        IObjectStorageService storage,
        ImportRuleService importRules,
        ClassificationRuleService classificationRules,
        StorageFileService storageFiles)
    {
        _currentUser = currentUser;
        _db = db;
        _storage = storage;
        _importRules = importRules;
        _classificationRules = classificationRules;
        _storageFiles = storageFiles;
    }

    public string BuildRawImportPrefix(Guid userId, Guid importBatchId)
    {
        return $"imports/raw/{userId:D}/{importBatchId:D}";
    }

    public async Task<IReadOnlyList<ImportBatchDto>> ListAsync(CancellationToken cancellationToken)
    {
        var batches = await _db.QuerySelect<ImportBatch>()
            .From<ImportBatch>()
            .SelectAllFrom<ImportBatch>()
            .Where(batch => batch.UserId == _currentUser.UserId)
            .ToListAsync();
        return batches.OrderByDescending(batch => batch.CreatedAt).Select(ToDto).ToList();
    }

    public async Task<UploadImportResponse> UploadAsync(Guid accountId, string? institution, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length <= 0 || file.Length > MaxImportBytes)
        {
            throw new ArgumentException("Import file must be between 1 byte and 10 MB.");
        }

        if (!AllowedContentTypes.Contains(file.ContentType) && !file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only CSV import files are supported in this phase.");
        }

        var account = await _db.GetByIdAsync<Account>(accountId, depth: 0);
        if (account?.UserId != _currentUser.UserId)
        {
            throw new ArgumentException("Account was not found.");
        }

        var batchId = Guid.NewGuid();
        var safeFileName = Path.GetFileName(file.FileName);
        var objectKey = $"{BuildRawImportPrefix(_currentUser.UserId, batchId)}/{safeFileName}";

        await using (var stream = file.OpenReadStream())
        {
            await _storage.PutAsync(objectKey, stream, file.ContentType, cancellationToken);
        }

        return await CreateBatchAsync(account, institution, safeFileName, file.ContentType, objectKey, JsonSerializer.Serialize(new { file.Length }), batchId, cancellationToken);
    }

    public async Task<UploadImportResponse> CreateFromStorageAsync(Guid accountId, string? institution, string fileName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Stored file name is required.");
        }

        var account = await _db.GetByIdAsync<Account>(accountId, depth: 0);
        if (account?.UserId != _currentUser.UserId)
        {
            throw new ArgumentException("Account was not found.");
        }

        var stored = await _storageFiles.FindByNameAsync(fileName, cancellationToken);
        if (stored is null)
        {
            throw new ArgumentException("Stored file was not found.");
        }

        return await CreateBatchAsync(
            account,
            institution,
            stored.StoredFileName,
            stored.ContentType,
            stored.S3ObjectKey,
            JsonSerializer.Serialize(new { storageFileId = stored.Id, stored.SizeBytes }),
            Guid.NewGuid(),
            cancellationToken);
    }

    public async Task<ParsedImportDto?> ParseAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var batch = await GetOwnedBatchAsync(batchId, cancellationToken: cancellationToken);
        if (batch is null)
        {
            return null;
        }

        var rows = await ReadCsvRowsAsync(batch, cancellationToken);
        batch.Status = "parsed";
        batch.RowCount = rows.Rows.Count;
        batch.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveAsync(batch, auditUserId: _currentUser.UserId.ToString());

        var templates = await _db.QuerySelect<ImportTemplate>()
            .From<ImportTemplate>()
            .SelectAllFrom<ImportTemplate>()
            .Where(template => template.UserId == _currentUser.UserId)
            .ToListAsync();
        var matchingTemplates = templates
            .Where(template => string.Equals(template.Institution, batch.Institution, StringComparison.OrdinalIgnoreCase))
            .Select(ToTemplateDto)
            .ToList();

        return new ParsedImportDto(
            batch.Id,
            rows.Columns,
            rows.Rows.Take(10).Select(row => (IReadOnlyDictionary<string, string>)row).ToList(),
            matchingTemplates);
    }

    public async Task<IReadOnlyList<ImportPreviewRowDto>?> PreviewAsync(Guid batchId, PreviewImportRequest request, CancellationToken cancellationToken)
    {
        var batch = await GetOwnedBatchAsync(batchId, cancellationToken: cancellationToken);
        if (batch is null)
        {
            return null;
        }

        if (request.RuleSetId is null)
        {
            ValidateColumnMap(request.ColumnMap);
        }
        var csv = await ReadCsvRowsAsync(batch, cancellationToken);
        var importRules = await _importRules.LoadRulesAsync(includeInactive: false, cancellationToken);
        var mappingRules = await _importRules.LoadMappingRulesAsync(request.RuleSetId, cancellationToken);
        var classificationRules = await _classificationRules.LoadRulesAsync(includeInactive: false, cancellationToken);
        var existingHashes = await LoadExistingImportHashesAsync(batch.AccountId, cancellationToken: cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var previewRows = new List<ImportPreviewRow>();

        await using (var transaction = _db.BeginMultiTransaction())
        {
            transaction.Open();

            try
            {
                var existingPreviewRows = await transaction.QuerySelect<ImportPreviewRow>()
                    .From<ImportPreviewRow>()
                    .SelectAllFrom<ImportPreviewRow>()
                    .Where(row => row.ImportBatchId == batch.Id)
                    .ToListAsync();
                foreach (var existingPreviewRow in existingPreviewRows)
                {
                    await transaction.Delete(existingPreviewRow);
                }

                var rowNumber = 1;
                foreach (var raw in csv.Rows)
                {
                    var (effectiveRaw, effectiveMap) = ApplyFieldMappingRules(raw, request.ColumnMap, mappingRules);
                    var mapped = MapRow(effectiveRaw, effectiveMap, request.DateFormat);
                    var application = ImportRuleService.ApplyRules(mapped.Description ?? string.Empty, importRules);
                    var cleaned = string.IsNullOrWhiteSpace(application.CleanedDescription) ? mapped.Description : application.CleanedDescription;
                    var type = application.Type ?? mapped.Type ?? InferType(mapped.AmountSigned);
                    var amount = Math.Abs(mapped.AmountSigned ?? 0);
                    var category = application.Category ?? mapped.Category;
                    var classification = application.Classification != "unknown"
                        ? application.Classification
                        : mapped.Classification ?? "unknown";

                    var classificationResult = ClassificationRuleService.Apply(cleaned ?? string.Empty, mapped.Merchant, category, amount, classificationRules);
                    if (classificationResult.Rule is not null)
                    {
                        classification = classificationResult.Classification;
                        category = classificationResult.Category;
                    }

                    var errors = ValidatePreviewRow(mapped.Date, amount, type, classification, cleaned);
                    var importHash = errors.Count == 0
                        ? BuildImportHash(batch.AccountId, mapped.Date!.Value, amount, cleaned!)
                        : null;
                    var duplicate = importHash is not null && (!seenHashes.Add(importHash) || existingHashes.Contains(importHash));
                    
                    var row = new ImportPreviewRow
                    {
                        Id = Guid.NewGuid(),
                        ImportBatchId = batch.Id,
                        RowNumber = rowNumber++,
                        RawData = JsonSerializer.Serialize(raw),
                        RawDescription = mapped.Description,
                        CleanedDescription = cleaned,
                        Date = mapped.Date,
                        Amount = amount,
                        Type = type,
                        Category = category,
                        Classification = classification,
                        ImportHash = importHash,
                        IsDuplicate = duplicate,
                        Accepted = errors.Count == 0 && !duplicate,
                        Errors = JsonSerializer.Serialize(errors),
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    //row.Type = "expense";

                    await transaction.Save(row);
                    previewRows.Add(row);
                }

                batch.Status = "previewed";
                batch.RowCount = previewRows.Count;
                batch.AcceptedCount = previewRows.Count(row => row.Accepted);
                batch.DuplicateCount = previewRows.Count(row => row.IsDuplicate);
                batch.ErrorCount = previewRows.Count(row => ReadErrors(row.Errors).Count > 0);
                batch.UpdatedAt = DateTimeOffset.UtcNow;
                await transaction.Save(batch);

                if (request.SaveTemplate)
                {
                    await SaveTemplateAsync(batch, request, now, transaction, cancellationToken);
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        return previewRows.OrderBy(row => row.RowNumber).Select(ToPreviewDto).ToList();
    }

    public async Task<IReadOnlyList<ImportPreviewRowDto>?> GetPreviewAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var batch = await GetOwnedBatchAsync(batchId, cancellationToken: cancellationToken);
        if (batch is null)
        {
            return null;
        }

        var rows = await _db.QuerySelect<ImportPreviewRow>()
            .From<ImportPreviewRow>()
            .SelectAllFrom<ImportPreviewRow>()
            .Where(row => row.ImportBatchId == batch.Id)
            .ToListAsync();
        return rows.OrderBy(row => row.RowNumber).Select(ToPreviewDto).ToList();
    }

    public async Task<ImportPreviewRowDto?> UpdatePreviewRowAsync(Guid batchId, Guid rowId, UpdateImportPreviewRowRequest request, CancellationToken cancellationToken)
    {
        var batch = await GetOwnedBatchAsync(batchId, cancellationToken: cancellationToken);
        if (batch is null)
        {
            return null;
        }

        var row = await _db.GetByIdAsync<ImportPreviewRow>(rowId, depth: 0);
        if (row?.ImportBatchId != batch.Id)
        {
            return null;
        }

        row.CleanedDescription = request.CleanedDescription ?? row.CleanedDescription;
        row.Date = request.Date ?? row.Date;
        row.Amount = request.Amount ?? row.Amount;
        row.Type = request.Type ?? row.Type;
        row.Category = request.Category ?? row.Category;
        row.Classification = request.Classification ?? row.Classification;
        row.Accepted = request.Accepted ?? row.Accepted;
        row.Errors = JsonSerializer.Serialize(ValidatePreviewRow(row.Date, row.Amount ?? 0, row.Type, row.Classification, row.CleanedDescription));
        row.ImportHash = ReadErrors(row.Errors).Count == 0 && row.Date.HasValue && row.Amount.HasValue && !string.IsNullOrWhiteSpace(row.CleanedDescription)
            ? BuildImportHash(batch.AccountId, row.Date.Value, row.Amount.Value, row.CleanedDescription)
            : null;
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveAsync(row, auditUserId: _currentUser.UserId.ToString());
        await RefreshBatchCountsAsync(batch, cancellationToken);
        return ToPreviewDto(row);
    }

    public async Task<ImportCommitResult?> CommitAsync(Guid batchId, CancellationToken cancellationToken)
    {
        await using var transaction = _db.BeginMultiTransaction();
        transaction.Open();

        try
        {
            var batch = await GetOwnedBatchAsync(batchId, transaction, cancellationToken);
            if (batch is null)
            {
                return null;
            }

            var rows = (await transaction.QuerySelect<ImportPreviewRow>()
                    .From<ImportPreviewRow>()
                    .SelectAllFrom<ImportPreviewRow>()
                    .Where(row => row.ImportBatchId == batch.Id)
                    .ToListAsync())
                .OrderBy(row => row.RowNumber)
                .ToList();
            var existingHashes = await LoadExistingImportHashesAsync(batch.AccountId, transaction, cancellationToken);
            var account = await transaction.GetByIdAsync<Account>(batch.AccountId)
                ?? throw new InvalidOperationException("Import account was not found.");

            var imported = 0;
            var skippedDuplicates = 0;
            var rejected = 0;
            var errors = 0;
            var now = DateTimeOffset.UtcNow;

            foreach (var row in rows)
            {
                var rowErrors = ReadErrors(row.Errors);
                if (!row.Accepted)
                {
                    rejected++;
                    continue;
                }

                if (rowErrors.Count > 0 || row.Date is null || row.Amount is null || string.IsNullOrWhiteSpace(row.Type) || string.IsNullOrWhiteSpace(row.CleanedDescription))
                {
                    errors++;
                    continue;
                }

                if (row.ImportHash is not null && existingHashes.Contains(row.ImportHash))
                {
                    skippedDuplicates++;
                    continue;
                }

                var transactionEntity = new FinancialTransaction
                {
                    Id = Guid.NewGuid(),
                    UserId = _currentUser.UserId,
                    AccountId = batch.AccountId,
                    Date = row.Date.Value,
                    Description = row.CleanedDescription,
                    Type = row.Type,
                    Classification = row.Classification,
                    Category = row.Category,
                    Amount = row.Amount.Value,
                    Currency = account.Currency,
                    Direction = DirectionForType(row.Type),
                    Status = "posted",
                    Source = "import",
                    ImportHash = row.ImportHash,
                    IsVoid = false,
                    IsSplit = false,
                    Metadata = JsonSerializer.Serialize(new { importBatchId = batch.Id, previewRowId = row.Id }),
                    CreatedAt = now,
                    UpdatedAt = now
                };

                await transaction.Save(transactionEntity);
                if (row.ImportHash is not null)
                {
                    existingHashes.Add(row.ImportHash);
                }
                imported++;
            }

            batch.Status = "committed";
            batch.AcceptedCount = imported;
            batch.DuplicateCount = skippedDuplicates;
            batch.ErrorCount = errors;
            batch.UpdatedAt = DateTimeOffset.UtcNow;
            await transaction.Save(batch);

            var result = new ImportCommitResult(imported, skippedDuplicates, rejected, errors);
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task SaveTemplateAsync(
        ImportBatch batch,
        PreviewImportRequest request,
        DateTimeOffset now,
        MultiTransaction transaction,
        CancellationToken cancellationToken)
    {
        var template = new ImportTemplate
        {
            Id = Guid.NewGuid(),
            UserId = _currentUser.UserId,
            Institution = batch.Institution,
            Name = string.IsNullOrWhiteSpace(request.TemplateName) ? $"{batch.Institution ?? "Import"} CSV" : request.TemplateName.Trim(),
            ColumnMap = JsonSerializer.Serialize(request.ColumnMap),
            DateFormat = request.DateFormat,
            AmountFormat = request.AmountFormat,
            CreatedAt = now,
            UpdatedAt = now
        };

        await transaction.Save(template);
    }

    private async Task<UploadImportResponse> CreateBatchAsync(
        Account account,
        string? institution,
        string fileName,
        string contentType,
        string objectKey,
        string metadata,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var batch = new ImportBatch
        {
            Id = batchId,
            UserId = _currentUser.UserId,
            AccountId = account.Id,
            Institution = string.IsNullOrWhiteSpace(institution) ? account.Institution : institution.Trim(),
            OriginalFileName = fileName,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "text/csv" : contentType,
            S3ObjectKey = objectKey,
            Status = "uploaded",
            Metadata = metadata,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _db.SaveAsync(batch, auditUserId: _currentUser.UserId.ToString());
        return new UploadImportResponse(batch.Id, batch.AccountId, batch.OriginalFileName, batch.ContentType, batch.S3ObjectKey, batch.Status);
    }

    private async Task RefreshBatchCountsAsync(ImportBatch batch, CancellationToken cancellationToken)
    {
        var rows = await _db.QuerySelect<ImportPreviewRow>()
            .From<ImportPreviewRow>()
            .SelectAllFrom<ImportPreviewRow>()
            .Where(row => row.ImportBatchId == batch.Id)
            .ToListAsync();
        batch.AcceptedCount = rows.Count(row => row.Accepted);
        batch.DuplicateCount = rows.Count(row => row.IsDuplicate);
        batch.ErrorCount = rows.Count(row => ReadErrors(row.Errors).Count > 0);
        batch.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveAsync(batch, auditUserId: _currentUser.UserId.ToString());
    }

    private async Task<ImportBatch?> GetOwnedBatchAsync(
        Guid batchId,
        MultiTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var batch = transaction is null
            ? await _db.GetByIdAsync<ImportBatch>(batchId, depth: 0)
            : await transaction.GetByIdAsync<ImportBatch>(batchId);
        return batch?.UserId == _currentUser.UserId ? batch : null;
    }

    private async Task<HashSet<string>> LoadExistingImportHashesAsync(
        Guid accountId,
        MultiTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var query = transaction is null
            ? _db.QuerySelect<FinancialTransaction>()
            : transaction.QuerySelect<FinancialTransaction>();
        var transactions = await query
            .From<FinancialTransaction>()
            .SelectAllFrom<FinancialTransaction>()
            .Where(item => item.UserId == _currentUser.UserId && item.AccountId == accountId)
            .ToListAsync();
        return transactions
            .Select(transaction => transaction.ImportHash)
            .Where(hash => !string.IsNullOrWhiteSpace(hash))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;
    }

    private async Task<CsvRows> ReadCsvRowsAsync(ImportBatch batch, CancellationToken cancellationToken)
    {
        var storedObject = await _storage.GetAsync(batch.S3ObjectKey, cancellationToken);
        using var reader = new StreamReader(storedObject.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        var rows = SimpleCsvParser.Parse(await reader.ReadToEndAsync(cancellationToken));
        if (rows.Columns.Count == 0)
        {
            throw new ArgumentException("CSV file does not include a header row.");
        }

        return rows;
    }

    private static MappedImportRow MapRow(IReadOnlyDictionary<string, string> row, ImportColumnMap map, string? dateFormat)
    {
        var signedAmount = ReadDecimal(row, map.Amount);
        var debit = ReadDecimal(row, map.Debit);
        var credit = ReadDecimal(row, map.Credit);

        if (signedAmount is null)
        {
            signedAmount = credit ?? (debit.HasValue ? -Math.Abs(debit.Value) : null);
        }

        return new MappedImportRow(
            ReadDate(row, map.Date, dateFormat),
            ReadValue(row, map.Description),
            ReadValue(row, map.Merchant),
            signedAmount,
            EmptyToNull(ReadValue(row, map.Type)),
            EmptyToNull(ReadValue(row, map.Category)),
            EmptyToNull(ReadValue(row, map.Classification)));
    }

    private static (IReadOnlyDictionary<string, string> Row, ImportColumnMap Map) ApplyFieldMappingRules(
        IReadOnlyDictionary<string, string> row,
        ImportColumnMap map,
        IReadOnlyList<ImportRule> rules)
    {
        if (rules.Count == 0)
        {
            return (row, map);
        }

        var effectiveRow = new Dictionary<string, string>(row, StringComparer.OrdinalIgnoreCase);
        var effectiveMap = map;

        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.SourceField) || string.IsNullOrWhiteSpace(rule.TargetField))
            {
                continue;
            }

            var value = ReadValue(row, rule.SourceField);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (rule.ValueTransform == "amount_negative" && !value.TrimStart().StartsWith("-", StringComparison.Ordinal))
            {
                value = $"-{value}";
            }
            else if (rule.ValueTransform == "amount_positive")
            {
                value = value.TrimStart('-');
            }

            var syntheticColumn = $"rule:{rule.TargetField}";
            effectiveRow[syntheticColumn] = value;
            effectiveMap = SetMappedColumn(effectiveMap, rule.TargetField, syntheticColumn);
        }

        return (effectiveRow, effectiveMap);
    }

    private static ImportColumnMap SetMappedColumn(ImportColumnMap map, string targetField, string column)
    {
        return targetField switch
        {
            "date" => map with { Date = column },
            "description" => map with { Description = column },
            "merchant" => map with { Merchant = column },
            "amount" => map with { Amount = column, Debit = null, Credit = null },
            "type" => map with { Type = column },
            "category" => map with { Category = column },
            "classification" => map with { Classification = column },
            _ => map
        };
    }

    private static List<string> ValidatePreviewRow(DateOnly? date, decimal amount, string? type, string? classification, string? description)
    {
        var errors = new List<string>();
        if (date is null)
        {
            errors.Add("Date is required or could not be parsed.");
        }

        if (amount <= 0)
        {
            errors.Add("Amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(type) || !FinanceValues.TransactionTypes.Contains(type))
        {
            errors.Add("Transaction type is invalid.");
        }

        if (string.IsNullOrWhiteSpace(classification) || !FinanceValues.Classifications.Contains(classification))
        {
            errors.Add("Classification is invalid.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            errors.Add("Description is required.");
        }

        return errors;
    }

    private static void ValidateColumnMap(ImportColumnMap map)
    {
        if (string.IsNullOrWhiteSpace(map.Date) || string.IsNullOrWhiteSpace(map.Description))
        {
            throw new ArgumentException("Date and description columns are required.");
        }

        if (string.IsNullOrWhiteSpace(map.Amount) && (string.IsNullOrWhiteSpace(map.Debit) || string.IsNullOrWhiteSpace(map.Credit)))
        {
            throw new ArgumentException("Map either a signed amount column or both debit and credit columns.");
        }
    }

    private static string BuildImportHash(Guid accountId, DateOnly date, decimal amount, string description)
    {
        var stable = $"{accountId:N}|{date:yyyy-MM-dd}|{amount:0.0000}|{NormalizeDescription(description)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stable))).ToLowerInvariant();
    }

    private static string NormalizeDescription(string description)
    {
        return string.Join(' ', description.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string InferType(decimal? signedAmount)
    {
        return signedAmount >= 0 ? "income" : "expense";
    }

    private static string DirectionForType(string type)
    {
        return type switch
        {
            "income" or "opening_balance" => "inflow",
            "expense" => "outflow",
            _ => "neutral"
        };
    }

    private static string? ReadValue(IReadOnlyDictionary<string, string> row, string? column)
    {
        return string.IsNullOrWhiteSpace(column) || !row.TryGetValue(column, out var value) ? null : value.Trim();
    }

    private static decimal? ReadDecimal(IReadOnlyDictionary<string, string> row, string? column)
    {
        var value = ReadValue(row, column);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Replace("$", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        if (value.StartsWith('(') && value.EndsWith(')'))
        {
            value = $"-{value[1..^1]}";
        }

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : null;
    }

    private static DateOnly? ReadDate(IReadOnlyDictionary<string, string> row, string? column, string? dateFormat)
    {
        var value = ReadValue(row, column);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(dateFormat)
            && DateOnly.TryParseExact(value, dateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
    }

    private static IReadOnlyList<string> ReadErrors(string errors)
    {
        return JsonSerializer.Deserialize<IReadOnlyList<string>>(errors) ?? [];
    }

    private static IReadOnlyDictionary<string, string> ReadRawData(string rawData)
    {
        return JsonSerializer.Deserialize<Dictionary<string, string>>(rawData) ?? new Dictionary<string, string>();
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static ImportBatchDto ToDto(ImportBatch batch)
    {
        return new ImportBatchDto(
            batch.Id,
            batch.AccountId,
            batch.Institution,
            batch.OriginalFileName,
            batch.ContentType,
            batch.S3ObjectKey,
            batch.Status,
            batch.RowCount,
            batch.AcceptedCount,
            batch.DuplicateCount,
            batch.ErrorCount,
            batch.CreatedAt,
            batch.UpdatedAt);
    }

    private static ImportTemplateDto ToTemplateDto(ImportTemplate template)
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(template.ColumnMap) ?? new Dictionary<string, string>();
        return new ImportTemplateDto(template.Id, template.Institution, template.Name, map, template.DateFormat, template.AmountFormat);
    }

    private static ImportPreviewRowDto ToPreviewDto(ImportPreviewRow row)
    {
        return new ImportPreviewRowDto(
            row.Id,
            row.RowNumber,
            ReadRawData(row.RawData),
            row.RawDescription,
            row.CleanedDescription,
            row.Date,
            row.Amount,
            row.Type,
            row.Category,
            row.Classification,
            row.ImportHash,
            row.IsDuplicate,
            row.Accepted,
            ReadErrors(row.Errors));
    }

    private sealed record MappedImportRow(
        DateOnly? Date,
        string? Description,
        string? Merchant,
        decimal? AmountSigned,
        string? Type,
        string? Category,
        string? Classification);
}

public sealed record CsvRows(
    IReadOnlyList<string> Columns,
    IReadOnlyList<Dictionary<string, string>> Rows);

public static class SimpleCsvParser
{
    public static CsvRows Parse(string content)
    {
        var records = new List<List<string>>();
        foreach (var record in ReadRecords(content))
        {
            if (record.Count > 0 && record.Any(value => !string.IsNullOrWhiteSpace(value)))
            {
                records.Add(record);
            }
        }

        if (records.Count == 0)
        {
            return new CsvRows([], []);
        }

        var columns = records[0].Select((column, index) => string.IsNullOrWhiteSpace(column) ? $"Column {index + 1}" : column.Trim()).ToList();
        var rows = records.Skip(1)
            .Select(record =>
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < columns.Count; i++)
                {
                    row[columns[i]] = i < record.Count ? record[i] : string.Empty;
                }

                return row;
            })
            .ToList();

        return new CsvRows(columns, rows);
    }

    private static IEnumerable<List<string>> ReadRecords(string content)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < content.Length; i++)
        {
            var ch = content[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < content.Length && content[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(ch);
                }
                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (ch == '\n')
            {
                row.Add(field.ToString().TrimEnd('\r'));
                field.Clear();
                yield return row;
                row = [];
            }
            else
            {
                field.Append(ch);
            }
        }

        row.Add(field.ToString().TrimEnd('\r'));
        yield return row;
    }
}
