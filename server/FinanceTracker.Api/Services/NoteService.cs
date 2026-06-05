using FinanceTracker.Api.Features.Organization;
using FinanceTracker.Api.Features.Transactions;
using PipelineRunner.Services;

namespace FinanceTracker.Api.Services;

public sealed class NoteService
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;
    public NoteService(ICurrentUserContext currentUser, IOrmMapperService db) { _currentUser = currentUser; _db = db; }

    public async Task<IReadOnlyList<NoteDto>> ListAsync(string? status, CancellationToken cancellationToken)
        => (await LoadOwnedAsync()).Where(note => string.IsNullOrWhiteSpace(status) || note.Status == status).OrderBy(note => note.Status).ThenBy(note => note.RemindOn).ThenByDescending(note => note.CreatedAt).Select(ToDto).ToList();

    public async Task<NoteDto> CreateAsync(UpsertNoteRequest request, CancellationToken cancellationToken)
    {
        Validate(request); var now = DateTimeOffset.UtcNow; var note = new FinanceNote { UserId = _currentUser.UserId, Status = "unmatched", CreatedAt = now, UpdatedAt = now };
        Apply(note, request); await _db.SaveAsync(note, auditUserId: _currentUser.UserId.ToString()); return ToDto(note);
    }

    public async Task<NoteDto?> UpdateAsync(Guid id, UpsertNoteRequest request, CancellationToken cancellationToken)
    {
        Validate(request); var note = await GetOwnedAsync(id); if (note is null) return null; Apply(note, request); note.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveAsync(note, auditUserId: _currentUser.UserId.ToString()); return ToDto(note);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) { var note = await GetOwnedAsync(id); return note is not null && await _db.DeleteAsync(note, userId: _currentUser.UserId.ToString()); }

    public async Task<IReadOnlyList<NoteMatchSuggestionDto>?> MatchAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        var transaction = await _db.GetByIdAsync<FinancialTransaction>(transactionId, depth: 0);
        if (transaction?.UserId != _currentUser.UserId) return null;
        return (await LoadOwnedAsync()).Where(note => note.Status == "unmatched").Select(note => Score(note, transaction)).Where(match => match.Score > 0).OrderByDescending(match => match.Score).ToList();
    }

    public async Task<NoteDto?> AcceptMatchAsync(Guid id, Guid transactionId, CancellationToken cancellationToken)
    {
        var note = await GetOwnedAsync(id); var transaction = await _db.GetByIdAsync<FinancialTransaction>(transactionId, depth: 0);
        if (note is null || transaction?.UserId != _currentUser.UserId) return null;
        note.MatchedTransactionId = transactionId; note.Status = "matched"; note.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveAsync(note, auditUserId: _currentUser.UserId.ToString()); return ToDto(note);
    }

    public async Task<NoteDto?> DismissAsync(Guid id, CancellationToken cancellationToken)
    {
        var note = await GetOwnedAsync(id); if (note is null) return null; note.Status = "dismissed"; note.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveAsync(note, auditUserId: _currentUser.UserId.ToString()); return ToDto(note);
    }

    private static NoteMatchSuggestionDto Score(FinanceNote note, FinancialTransaction transaction)
    {
        var score = 0; var reasons = new List<string>(); var text = $"{transaction.Merchant} {transaction.Description}";
        if (!string.IsNullOrWhiteSpace(note.MerchantHint) && text.Contains(note.MerchantHint, StringComparison.OrdinalIgnoreCase)) { score += 50; reasons.Add("merchant"); }
        if (note.AmountHint.HasValue && note.AmountHint.Value > 0 && Math.Abs(transaction.Amount - note.AmountHint.Value) <= note.AmountHint.Value * 0.05m) { score += 30; reasons.Add("amount"); }
        if (note.DateHint.HasValue && Math.Abs(transaction.Date.DayNumber - note.DateHint.Value.DayNumber) <= 7) { score += 20; reasons.Add("date"); }
        return new NoteMatchSuggestionDto(ToDto(note), score, reasons);
    }

    private async Task<List<FinanceNote>> LoadOwnedAsync() => await _db.QuerySelect<FinanceNote>().From<FinanceNote>().SelectAllFrom<FinanceNote>().Where(note => note.UserId == _currentUser.UserId).ToListAsync();
    private async Task<FinanceNote?> GetOwnedAsync(Guid id) { var note = await _db.GetByIdAsync<FinanceNote>(id, depth: 0); return note?.UserId == _currentUser.UserId ? note : null; }
    private static void Apply(FinanceNote note, UpsertNoteRequest request) { note.Title = request.Title.Trim(); note.Body = EmptyToNull(request.Body); note.AmountHint = request.AmountHint; note.MerchantHint = EmptyToNull(request.MerchantHint); note.DateHint = request.DateHint; note.RemindOn = request.RemindOn; }
    private static void Validate(UpsertNoteRequest request) { if (string.IsNullOrWhiteSpace(request.Title)) throw new ArgumentException("Note title is required."); if (request.AmountHint < 0) throw new ArgumentException("Amount hint cannot be negative."); }
    private static NoteDto ToDto(FinanceNote note) => new(note.Id, note.Title, note.Body, note.AmountHint, note.MerchantHint, note.DateHint, note.MatchedTransactionId, note.Status, note.RemindOn, note.CreatedAt, note.UpdatedAt);
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
