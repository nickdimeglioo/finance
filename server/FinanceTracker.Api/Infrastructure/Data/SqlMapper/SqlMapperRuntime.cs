using FinanceTracker.Api.Configuration;
using Mappers.DapperUtils;
using System.Data;

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
        OrmMapper.Configure(options =>
        {
            options.TableStyle = DbNamingConvention.SnakeCase;
            options.ColumnStyle = DbNamingConvention.SnakeCase;
            options.PluralizeTableNames = true;
            options.DefaultTransactionIsolationLevel = IsolationLevel.ReadCommitted;
        });

        //DapperFieldType.TryRegisterHandler();
        //DbConnection.Configure(connectionString ?? string.Empty);
    }
}
