using FinanceTracker.Api.Configuration;
using Mappers.DapperUtils;

namespace FinanceTracker.Api.Infrastructure.Data.SqlMapper;

public static class SqlMapperRuntime
{
    public static void Configure(WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration
            .GetSection(DatabaseOptions.SectionName)
            .Get<DatabaseOptions>()?
            .ConnectionString;

        Configure(connectionString);
    }

    public static void Configure(string? connectionString)
    {
        DapperFieldType.RegisterDefaultHandlers();
        DbConnection.Configure(connectionString ?? string.Empty);
    }
}
