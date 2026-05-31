using FinanceTracker.Api.Infrastructure.Data;
using FinanceTracker.Api.Infrastructure.Storage;
using Npgsql;

namespace FinanceTracker.Api.Services;

public sealed class HealthDiagnosticService
{
    private readonly NpgsqlConnectionFactory _connectionFactory;
    private readonly IObjectStorageService _storage;

    public HealthDiagnosticService(NpgsqlConnectionFactory connectionFactory, IObjectStorageService storage)
    {
        _connectionFactory = connectionFactory;
        _storage = storage;
    }

    public async Task<object> CheckAsync(CancellationToken cancellationToken)
    {
        var database = await CheckDatabaseAsync(cancellationToken);
        var storage = await _storage.CheckAsync(cancellationToken);
        var overall = database.Reachable && storage.Reachable ? "healthy" : "degraded";

        return new
        {
            status = overall,
            api = new { reachable = true },
            database,
            storage,
            storageConfiguration = _storage.GetConfiguration()
        };
    }

    private async Task<DatabaseDiagnosticResult> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("select 1", connection)
            {
                CommandTimeout = 2
            };

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return new DatabaseDiagnosticResult(true, Equals(result, 1), "PostgreSQL connection succeeded.");
        }
        catch (Exception ex)
        {
            return new DatabaseDiagnosticResult(true, false, ex.Message);
        }
    }
}

public sealed record DatabaseDiagnosticResult(bool Configured, bool Reachable, string Message);
