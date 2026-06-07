using System.Security.Cryptography;
using System.Text;
using FinanceTracker.Api.Features.Accounts;
using FinanceTracker.Api.Features.Imports;
using FinanceTracker.Api.Features.Rules;
using FinanceTracker.Api.Features.Shared;
using FinanceTracker.Api.Features.Transactions;
using PipelineRunner.Services;

namespace FinanceTracker.Api.Services;

public sealed class ImportOrchestrator
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;
    private readonly RulesetService _rulesets;
    private readonly CsvParserService _parser;
    private readonly MappingEngine _mapping;
    private readonly RulesetClassificationEngine _classification;
    private readonly DeduplicationService _deduplication;
    private readonly RecurringRuleService _recurringRules;
    private readonly TagService _tags;
    private readonly TransferLinkService _transferLinks;
    private readonly ILogger<ImportOrchestrator> _logger;

    public ImportOrchestrator(
        ICurrentUserContext currentUser,
        IOrmMapperService db,
        RulesetService rulesets,
        CsvParserService parser,
        MappingEngine mapping,
        RulesetClassificationEngine classification,
        DeduplicationService deduplication,
        RecurringRuleService recurringRules,
        TagService tags,
        TransferLinkService transferLinks,
        ILogger<ImportOrchestrator> logger)
    {
        _currentUser = currentUser;
        _db = db;
        _rulesets = rulesets;
        _parser = parser;
        _mapping = mapping;
        _classification = classification;
        _deduplication = deduplication;
        _recurringRules = recurringRules;
        _tags = tags;
        _transferLinks = transferLinks;
        _logger = logger;
    }

    public async Task<RulesetImportResult> PreviewAsync(RulesetImportRequest request, IFormFile file, CancellationToken cancellationToken)
    {
        return await RunAsync(request, file, isDryRun: true, cancellationToken);
    }

    public async Task<RulesetImportResult> RunAsync(RulesetImportRequest request, IFormFile file, CancellationToken cancellationToken)
    {
        return await RunAsync(request, file, isDryRun: false, cancellationToken);
    }

    public async Task<ImportJobDto?> GetJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _db.GetByIdAsync<ImportJob>(jobId, depth: 0);
        return job?.UserId == _currentUser.UserId ? ToDto(job) : null;
    }

    private async Task<RulesetImportResult> RunAsync(RulesetImportRequest request, IFormFile file, bool isDryRun, CancellationToken cancellationToken)
    {
        var account = await _db.GetByIdAsync<Account>(request.AccountId, depth: 0);
        if (account?.UserId != _currentUser.UserId)
        {
            throw new ArgumentException("Account was not found.");
        }

        var ruleset = await _rulesets.GetOwnedActiveEntityAsync(request.RulesetId, cancellationToken);
        if (ruleset is null)
        {
            throw new ArgumentException("Ruleset was not found.");
        }

        var started = DateTimeOffset.UtcNow;
        var job = new ImportJob
        {
            Id = Guid.NewGuid(),
            UserId = _currentUser.UserId,
            AccountId = account.Id,
            RulesetId = ruleset.Id,
            RulesetVersion = ruleset.Version,
            FileName = Path.GetFileName(file.FileName),
            Status = "processing",
            IsDryRun = isDryRun,
            StartedAt = started,
            CreatedAt = started,
            UpdatedAt = started
        };

        await _db.SaveAsync(job, auditUserId: _currentUser.UserId.ToString());

        try
        {
            var parsed = await _parser.ParseAsync(file, ruleset, cancellationToken);
            var mappedRows = _mapping.Map(parsed, ruleset);
            var classifiedRows = mappedRows.Select(row => _classification.Classify(row, ruleset)).ToList();
            if (!isDryRun && request.RowOverrides is not null)
            {
                classifiedRows = ApplyOverrides(classifiedRows, request.RowOverrides);
            }
            var existingUniqueIds = await _deduplication.LoadExistingUniqueIdsAsync(account.Id, transaction: null, cancellationToken);
            var seenUniqueIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var preview = await BuildPreviewAsync(account.Id, classifiedRows, existingUniqueIds, seenUniqueIds, cancellationToken);
            if (!isDryRun && request.AcceptedRowNumbers is not null)
            {
                var requestedAcceptedRows = request.AcceptedRowNumbers.ToHashSet();
                preview = preview
                    .Select(row => row with { Accepted = requestedAcceptedRows.Contains(row.RowNumber) && row.Errors.Count == 0 })
                    .ToList();
            }

            var committedRows = 0;
            if (!isDryRun)
            {
                committedRows = await CommitAsync(account, ruleset, classifiedRows, preview, cancellationToken);
                try
                {
                    await _recurringRules.MatchTransactionsAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Import {ImportJobId} committed, but recurring matching failed.", job.Id);
                }
            }

            var allErrors = parsed.Errors.Concat(preview.SelectMany(row => row.Errors)).ToList();
            var acceptedRows = preview.Count(row => row.Accepted);
            job.TotalRows = parsed.Rows.Count;
            job.SuccessRows = isDryRun ? acceptedRows : committedRows;
            job.SkippedRows = preview.Count(row => !row.Accepted) + (isDryRun ? 0 : acceptedRows - committedRows);
            job.ErrorRows = preview.Count(row => row.Errors.Count > 0) + parsed.Errors.Count;
            job.Errors = RulesetJson.Write(allErrors);
            job.Status = "complete";
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.UpdatedAt = job.CompletedAt.Value;
            await _db.SaveAsync(job, auditUserId: _currentUser.UserId.ToString());

            return new RulesetImportResult(ToDto(job), preview);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            job.Status = "failed";
            job.Errors = RulesetJson.Write(new[] { new ImportJobErrorDto(0, null, ex.Message) });
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.UpdatedAt = job.CompletedAt.Value;
            await _db.SaveAsync(job, auditUserId: _currentUser.UserId.ToString());
            throw;
        }
    }

    private async Task<int> CommitAsync(
        Account account,
        Ruleset ruleset,
        IReadOnlyList<ClassifiedRulesetTransaction> classifiedRows,
        IReadOnlyList<RulesetImportPreviewRowDto> preview,
        CancellationToken cancellationToken)
    {
        await using var transaction = _db.BeginMultiTransaction();
        transaction.Open();

        try
        {
            var existingUniqueIds = await _deduplication.LoadExistingUniqueIdsAsync(account.Id, transaction, cancellationToken);
            var acceptedPreviewByRow = preview.Where(row => row.Accepted).ToDictionary(row => row.RowNumber);
            var now = DateTimeOffset.UtcNow;
            var imported = 0;

            foreach (var classified in classifiedRows)
            {
                if (!acceptedPreviewByRow.TryGetValue(classified.Mapped.RowNumber, out var previewRow)
                    || string.IsNullOrWhiteSpace(classified.Mapped.UniqueId)
                    || classified.Mapped.Date is null
                    || classified.Mapped.Amount is null
                    || string.IsNullOrWhiteSpace(classified.Mapped.Type)
                    || string.IsNullOrWhiteSpace(classified.Mapped.Description))
                {
                    continue;
                }

                var commitUniqueId = ResolveCommitUniqueId(classified.Mapped.UniqueId, previewRow, existingUniqueIds);
                if (commitUniqueId is null)
                {
                    continue;
                }

                var entity = new FinancialTransaction
                {
                    Id = Guid.NewGuid(),
                    UserId = _currentUser.UserId,
                    AccountId = account.Id,
                    Date = classified.Mapped.Date.Value,
                    Description = classified.Mapped.Description,
                    Merchant = classified.Merchant,
                    Type = classified.Mapped.Type,
                    Classification = classified.Classification,
                    Category = classified.Category,
                    Subcategory = classified.Subcategory,
                    Amount = classified.Mapped.Amount.Value,
                    Currency = account.Currency,
                    Direction = DirectionForType(classified.Mapped.Type),
                    Status = "posted",
                    Source = "import",
                    ImportHash = BuildHash(account.Id, commitUniqueId),
                    UniqueId = commitUniqueId,
                    RulesetId = ruleset.Id,
                    RulesetVersion = ruleset.Version,
                    MatchedClassificationRuleId = classified.MatchedRuleIds.FirstOrDefault(),
                    Tags = RulesetJson.Write(classified.Tags),
                    RawRow = RulesetJson.Write(classified.Mapped.RawRow),
                    IsVoid = false,
                    IsSplit = false,
                    Metadata = RulesetJson.Write(new { rulesetId = ruleset.Id, rulesetVersion = ruleset.Version }),
                    CreatedAt = now,
                    UpdatedAt = now
                };

                await transaction.Save(entity);
                await _transferLinks.ApplyImportLinkAsync(
                    entity,
                    classified.TransferTargetAccountId,
                    classified.TransferTargetAccountName,
                    classified.TransferLinkMode,
                    classified.TransferMatchWindowDays,
                    account,
                    transaction,
                    cancellationToken);
                if (classified.Tags.Count > 0)
                {
                    await _tags.AssignTagsByNameAsync(entity, classified.Tags, transaction, cancellationToken);
                }
                existingUniqueIds.Add(commitUniqueId);
                imported++;
            }

            await transaction.CommitAsync();
            return imported;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task<IReadOnlyList<RulesetImportPreviewRowDto>> BuildPreviewAsync(
        Guid accountId,
        IReadOnlyList<ClassifiedRulesetTransaction> rows,
        HashSet<string> existingUniqueIds,
        HashSet<string> seenUniqueIds,
        CancellationToken cancellationToken)
    {
        var preview = new List<RulesetImportPreviewRowDto>();
        foreach (var row in rows)
        {
            var errors = row.Mapped.Errors.ToList();
            var duplicate = false;
            if (!string.IsNullOrWhiteSpace(row.Mapped.UniqueId))
            {
                duplicate = existingUniqueIds.Contains(row.Mapped.UniqueId) || !seenUniqueIds.Add(row.Mapped.UniqueId);
            }

            if (!FinanceValues.Classifications.Contains(row.Classification))
            {
                errors.Add(new ImportJobErrorDto(row.Mapped.RowNumber, "classification", "Classification is invalid."));
            }

            var transferPreview = await _transferLinks.PreviewImportLinkAsync(
                accountId,
                row.Mapped.Date,
                row.Mapped.Amount,
                row.TransferTargetAccountId,
                row.TransferTargetAccountName,
                row.TransferLinkMode,
                row.TransferMatchWindowDays,
                transaction: null,
                cancellationToken);

            preview.Add(new RulesetImportPreviewRowDto(
                row.Mapped.RowNumber,
                row.Mapped.RawRow,
                row.Mapped.Date,
                row.Mapped.Amount,
                row.Mapped.Type,
                row.Mapped.Description,
                row.Merchant,
                row.Category,
                row.Subcategory,
                row.Classification,
                row.Tags,
                row.Mapped.UniqueId,
                duplicate,
                errors.Count == 0 && !duplicate,
                row.MatchedRuleIds,
                errors,
                transferPreview.TargetAccountId,
                transferPreview.TargetAccountName,
                transferPreview.LinkMode,
                transferPreview.MatchWindowDays,
                transferPreview.CandidateCount,
                transferPreview.Status,
                transferPreview.Message));
        }

        return preview;
    }

    private static List<ClassifiedRulesetTransaction> ApplyOverrides(
        IReadOnlyList<ClassifiedRulesetTransaction> rows,
        IReadOnlyList<RulesetImportRowOverrideDto> overrides)
    {
        var overridesByRow = overrides
            .GroupBy(item => item.RowNumber)
            .ToDictionary(group => group.Key, group => group.Last());

        return rows.Select(row =>
        {
            if (!overridesByRow.TryGetValue(row.Mapped.RowNumber, out var item))
            {
                return row;
            }

            var type = NormalizeValue(item.Type)?.ToLowerInvariant();
            var description = NormalizeValue(item.Description);
            var classification = NormalizeValue(item.Classification)?.ToLowerInvariant() ?? "unknown";
            var transferLinkMode = NormalizeTransferLinkMode(item.TransferLinkMode);
            var transferMatchWindowDays = NormalizeWindow(item.TransferMatchWindowDays);
            decimal? amount = item.Amount is null ? null : Math.Abs(item.Amount.Value);
            var uniqueId = row.Mapped.UniqueId;
            if (string.IsNullOrWhiteSpace(uniqueId) && item.Date.HasValue && amount > 0 && !string.IsNullOrWhiteSpace(description))
            {
                uniqueId = $"{item.Date.Value:yyyy-MM-dd}|{amount.Value:0.0000}|{description.Trim().ToUpperInvariant()}";
            }
            var errors = row.Mapped.Errors
                .Where(error => error.Column is not ("date" or "amount" or "type" or "description" or "classification"))
                .ToList();

            if (item.Date is null)
            {
                errors.Add(new ImportJobErrorDto(item.RowNumber, "date", "Date is required."));
            }
            if (amount is null || amount <= 0)
            {
                errors.Add(new ImportJobErrorDto(item.RowNumber, "amount", "Amount must be greater than zero."));
            }
            if (string.IsNullOrWhiteSpace(type) || !FinanceValues.TransactionTypes.Contains(type))
            {
                errors.Add(new ImportJobErrorDto(item.RowNumber, "type", "Transaction type is invalid."));
            }
            if (string.IsNullOrWhiteSpace(description))
            {
                errors.Add(new ImportJobErrorDto(item.RowNumber, "description", "Description is required."));
            }
            if (!FinanceValues.Classifications.Contains(classification))
            {
                errors.Add(new ImportJobErrorDto(item.RowNumber, "classification", "Classification is invalid."));
            }

            var mapped = row.Mapped with
            {
                Date = item.Date,
                Amount = amount,
                Type = type,
                Description = description,
                Merchant = NormalizeValue(item.Merchant),
                Category = NormalizeValue(item.Category),
                Subcategory = NormalizeValue(item.Subcategory),
                Classification = classification,
                Tags = NormalizeTags(item.Tags),
                UniqueId = uniqueId,
                Errors = errors
            };

            return row with
            {
                Mapped = mapped,
                Merchant = mapped.Merchant,
                Category = mapped.Category,
                Subcategory = mapped.Subcategory,
                Classification = classification,
                Tags = mapped.Tags,
                TransferTargetAccountId = item.TransferTargetAccountId,
                TransferTargetAccountName = NormalizeValue(item.TransferTargetAccountName),
                TransferLinkMode = transferLinkMode,
                TransferMatchWindowDays = transferMatchWindowDays
            };
        }).ToList();
    }

    private static string? NormalizeValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string>? tags)
    {
        return (tags ?? [])
            .Select(NormalizeValue)
            .OfType<string>()
            .Select(tag => tag.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NormalizeTransferLinkMode(string? value)
    {
        var normalized = NormalizeValue(value)?.ToLowerInvariant();
        return normalized is "auto" or "suggest" ? normalized : null;
    }

    private static int NormalizeWindow(int? value)
    {
        return Math.Clamp(value is null or <= 0 ? 7 : value.Value, 0, 45);
    }

    private static ImportJobDto ToDto(ImportJob job)
    {
        return new ImportJobDto(
            job.Id,
            job.AccountId,
            job.RulesetId,
            job.RulesetVersion,
            job.FileName,
            job.Status,
            job.TotalRows,
            job.SuccessRows,
            job.SkippedRows,
            job.ErrorRows,
            RulesetJson.Read<IReadOnlyList<ImportJobErrorDto>>(job.Errors, []),
            job.IsDryRun,
            job.StartedAt,
            job.CompletedAt);
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

    private static string BuildHash(Guid accountId, string uniqueId)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{accountId:N}|{uniqueId}"))).ToLowerInvariant();
    }

    private static string? ResolveCommitUniqueId(
        string baseUniqueId,
        RulesetImportPreviewRowDto previewRow,
        HashSet<string> existingUniqueIds)
    {
        if (!existingUniqueIds.Contains(baseUniqueId))
        {
            return baseUniqueId;
        }

        if (!previewRow.IsDuplicate)
        {
            return null;
        }

        var overrideUniqueId = $"{baseUniqueId}|duplicate-row:{previewRow.RowNumber}";
        return existingUniqueIds.Contains(overrideUniqueId) ? null : overrideUniqueId;
    }
}
