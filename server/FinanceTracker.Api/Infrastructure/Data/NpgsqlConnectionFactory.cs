using FinanceTracker.Api.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FinanceTracker.Api.Infrastructure.Data;

public sealed class NpgsqlConnectionFactory
{
    private readonly DatabaseOptions _options;

    public NpgsqlConnectionFactory(IOptions<DatabaseOptions> options)
    {
        _options = options.Value;
    }

    public NpgsqlConnection CreateConnection()
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("Database connection string is not configured.");
        }

        return new NpgsqlConnection(_options.ConnectionString);
    }
}

