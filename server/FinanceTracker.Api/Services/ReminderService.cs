using FinanceTracker.Api.Features.Organization;
using PipelineRunner.Services;

namespace FinanceTracker.Api.Services;

public sealed class ReminderService
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;
    public ReminderService(ICurrentUserContext currentUser, IOrmMapperService db) { _currentUser = currentUser; _db = db; }

    public async Task<IReadOnlyList<ReminderDto>> ListAsync(bool includeResolved, CancellationToken cancellationToken)
    {
        await GenerateForUserAsync(_currentUser.UserId);
        var reminders = await _db.QuerySelect<Reminder>().From<Reminder>().SelectAllFrom<Reminder>().Where(reminder => reminder.UserId == _currentUser.UserId).ToListAsync();
        return reminders.Where(reminder => includeResolved || reminder.Status == "pending").OrderBy(reminder => reminder.DueOn).Select(ToDto).ToList();
    }

    public async Task<ReminderDto?> SetStatusAsync(Guid id, string status, CancellationToken cancellationToken)
    {
        if (status is not ("dismissed" or "completed")) throw new ArgumentException("Invalid reminder status.");
        var item = await _db.GetByIdAsync<Reminder>(id, depth: 0); if (item?.UserId != _currentUser.UserId) return null;
        item.Status = status; item.UpdatedAt = DateTimeOffset.UtcNow; await _db.SaveAsync(item, auditUserId: _currentUser.UserId.ToString()); return ToDto(item);
    }

    internal async Task GenerateAllDueAsync(CancellationToken cancellationToken)
    {
        var userIds = (await _db.QuerySelect<FinanceNote>().From<FinanceNote>().SelectAllFrom<FinanceNote>().ToListAsync()).Select(note => note.UserId)
            .Concat((await _db.QuerySelect<RecurringRule>().From<RecurringRule>().SelectAllFrom<RecurringRule>().ToListAsync()).Select(rule => rule.UserId)).Distinct();
        foreach (var userId in userIds) await GenerateForUserAsync(userId);
    }

    private async Task GenerateForUserAsync(Guid userId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow); var now = DateTimeOffset.UtcNow;
        var existing = await _db.QuerySelect<Reminder>().From<Reminder>().SelectAllFrom<Reminder>().Where(reminder => reminder.UserId == userId && reminder.Status == "pending").ToListAsync();
        var notes = await _db.QuerySelect<FinanceNote>().From<FinanceNote>().SelectAllFrom<FinanceNote>().Where(note => note.UserId == userId && note.Status == "unmatched").ToListAsync();
        foreach (var note in notes.Where(note => note.RemindOn.HasValue && note.RemindOn <= today))
            if (!existing.Any(item => item.Type == "note" && item.SourceId == note.Id)) await _db.SaveAsync(new Reminder { UserId = userId, Type = "note", SourceId = note.Id, Title = note.Title, Message = note.Body, DueOn = note.RemindOn!.Value, Status = "pending", CreatedAt = now, UpdatedAt = now });
        var rules = await _db.QuerySelect<RecurringRule>().From<RecurringRule>().SelectAllFrom<RecurringRule>().Where(rule => rule.UserId == userId && rule.IsActive == true).ToListAsync();
        foreach (var rule in rules.Where(rule => rule.NextExpected < today))
            if (!existing.Any(item => item.Type == "recurring_rule" && item.SourceId == rule.Id)) await _db.SaveAsync(new Reminder { UserId = userId, Type = "recurring_rule", SourceId = rule.Id, Title = $"Overdue: {rule.Name}", Message = $"Expected {rule.Amount:0.00} {rule.Currency}", DueOn = rule.NextExpected, Status = "pending", CreatedAt = now, UpdatedAt = now });
    }

    private static ReminderDto ToDto(Reminder item) => new(item.Id, item.Type, item.SourceId, item.Title, item.Message, item.DueOn, item.Status, item.CreatedAt, item.UpdatedAt);
}

public sealed class ReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReminderWorker> _logger;
    public ReminderWorker(IServiceScopeFactory scopeFactory, ILogger<ReminderWorker> logger) { _scopeFactory = scopeFactory; _logger = logger; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ReminderService>().GenerateAllDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The reminder generation cycle failed and will retry on the next cycle.");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
