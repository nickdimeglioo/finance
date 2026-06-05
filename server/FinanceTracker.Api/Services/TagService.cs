using FinanceTracker.Api.Features.Organization;
using FinanceTracker.Api.Features.Transactions;
using PipelineRunner.Services;

namespace FinanceTracker.Api.Services;

public sealed class TagService
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;

    public TagService(ICurrentUserContext currentUser, IOrmMapperService db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    public async Task<IReadOnlyList<TagDto>> ListAsync(CancellationToken cancellationToken)
    {
        var tags = await LoadOwnedTagsAsync();
        var joins = await _db.QuerySelect<TransactionTag>().From<TransactionTag>().SelectAllFrom<TransactionTag>().ToListAsync();
        var counts = joins.GroupBy(join => join.TagId).ToDictionary(group => group.Key, group => group.Count());
        return tags.OrderBy(tag => tag.Name).Select(tag => ToDto(tag, counts.GetValueOrDefault(tag.Id))).ToList();
    }

    public async Task<TagDto> CreateAsync(UpsertTagRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        await EnsureUniqueAsync(request.Name, null);
        var now = DateTimeOffset.UtcNow;
        var tag = new Tag { UserId = _currentUser.UserId, Name = request.Name.Trim(), Color = EmptyToNull(request.Color), CreatedAt = now, UpdatedAt = now };
        await _db.SaveAsync(tag, auditUserId: _currentUser.UserId.ToString());
        return ToDto(tag, 0);
    }

    public async Task<TagDto?> UpdateAsync(Guid id, UpsertTagRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var tag = await GetOwnedAsync(id);
        if (tag is null) return null;
        await EnsureUniqueAsync(request.Name, id);
        var oldName = tag.Name;
        tag.Name = request.Name.Trim();
        tag.Color = EmptyToNull(request.Color);
        tag.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveAsync(tag, auditUserId: _currentUser.UserId.ToString());
        if (!string.Equals(oldName, tag.Name, StringComparison.OrdinalIgnoreCase))
        {
            await ReplaceMirroredNameAsync(tag.Id, oldName, tag.Name);
        }
        var joins = await LoadJoinsAsync(tag.Id);
        return ToDto(tag, joins.Count);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var tag = await GetOwnedAsync(id);
        if (tag is null) return false;
        await using var transaction = _db.BeginMultiTransaction();
        transaction.Open();
        try
        {
            var joins = await transaction.QuerySelect<TransactionTag>().From<TransactionTag>().SelectAllFrom<TransactionTag>().Where(join => join.TagId == tag.Id).ToListAsync();
            foreach (var join in joins)
            {
                var entity = await transaction.GetByIdAsync<FinancialTransaction>(join.TransactionId);
                if (entity?.UserId == _currentUser.UserId)
                {
                    entity.Tags = RulesetJson.Write(ReadNames(entity.Tags).Where(name => !string.Equals(name, tag.Name, StringComparison.OrdinalIgnoreCase)).ToList());
                    entity.UpdatedAt = DateTimeOffset.UtcNow;
                    await transaction.Save(entity);
                }
                await transaction.Delete(join);
            }
            await transaction.Delete(tag);
            await transaction.CommitAsync();
            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<string>?> ReplaceTransactionTagsAsync(Guid transactionId, IReadOnlyList<Guid> tagIds, CancellationToken cancellationToken)
    {
        await using var transaction = _db.BeginMultiTransaction();
        transaction.Open();
        try
        {
            var entity = await transaction.GetByIdAsync<FinancialTransaction>(transactionId);
            if (entity?.UserId != _currentUser.UserId) return null;
            var distinctIds = tagIds.Distinct().ToList();
            var tags = new List<Tag>();
            foreach (var id in distinctIds)
            {
                var tag = await transaction.GetByIdAsync<Tag>(id);
                if (tag?.UserId != _currentUser.UserId) throw new ArgumentException("One or more tags were not found.");
                tags.Add(tag);
            }
            var existing = await transaction.QuerySelect<TransactionTag>().From<TransactionTag>().SelectAllFrom<TransactionTag>().Where(join => join.TransactionId == transactionId).ToListAsync();
            foreach (var join in existing) await transaction.Delete(join);
            var now = DateTimeOffset.UtcNow;
            foreach (var tag in tags) await transaction.Save(new TransactionTag { TransactionId = transactionId, TagId = tag.Id, CreatedAt = now });
            var names = tags.Select(tag => tag.Name).OrderBy(name => name).ToList();
            entity.Tags = RulesetJson.Write(names);
            entity.UpdatedAt = now;
            await transaction.Save(entity);
            await transaction.CommitAsync();
            return names;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    internal async Task<HashSet<Guid>> LoadTransactionIdsForTagAsync(Guid tagId)
    {
        var tag = await GetOwnedAsync(tagId);
        if (tag is null) return [];
        return (await LoadJoinsAsync(tagId)).Select(join => join.TransactionId).ToHashSet();
    }

    private async Task ReplaceMirroredNameAsync(Guid tagId, string oldName, string newName)
    {
        foreach (var join in await LoadJoinsAsync(tagId))
        {
            var entity = await _db.GetByIdAsync<FinancialTransaction>(join.TransactionId, depth: 0);
            if (entity?.UserId != _currentUser.UserId) continue;
            entity.Tags = RulesetJson.Write(ReadNames(entity.Tags).Select(name => string.Equals(name, oldName, StringComparison.OrdinalIgnoreCase) ? newName : name).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveAsync(entity, auditUserId: _currentUser.UserId.ToString());
        }
    }

    private async Task<List<Tag>> LoadOwnedTagsAsync() => await _db.QuerySelect<Tag>().From<Tag>().SelectAllFrom<Tag>().Where(tag => tag.UserId == _currentUser.UserId).ToListAsync();
    private async Task<List<TransactionTag>> LoadJoinsAsync(Guid tagId) => await _db.QuerySelect<TransactionTag>().From<TransactionTag>().SelectAllFrom<TransactionTag>().Where(join => join.TagId == tagId).ToListAsync();
    private async Task<Tag?> GetOwnedAsync(Guid id) { var tag = await _db.GetByIdAsync<Tag>(id, depth: 0); return tag?.UserId == _currentUser.UserId ? tag : null; }
    private async Task EnsureUniqueAsync(string name, Guid? exceptId) { if ((await LoadOwnedTagsAsync()).Any(tag => tag.Id != exceptId && string.Equals(tag.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Tag name already exists."); }
    private static void Validate(UpsertTagRequest request) { if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Tag name is required."); }
    private static IReadOnlyList<string> ReadNames(string json) => RulesetJson.Read<IReadOnlyList<string>>(json, []);
    private static TagDto ToDto(Tag tag, int count) => new(tag.Id, tag.Name, tag.Color, count, tag.CreatedAt, tag.UpdatedAt);
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
