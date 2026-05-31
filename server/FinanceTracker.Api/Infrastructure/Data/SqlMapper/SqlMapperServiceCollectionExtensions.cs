using Microsoft.Extensions.DependencyInjection;

namespace FinanceTracker.Api.Infrastructure.Data.SqlMapper;

public static class SqlMapperServiceCollectionExtensions
{
    public static IServiceCollection AddFinanceOrmMapper(this IServiceCollection services, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
        }

        SqlMapperRuntime.Configure(connectionString);

        services.AddScoped<IConnectionProvider>(_ => new FinanceConnectionProvider(connectionString));
        services.AddScoped<IOrmMapperService, FinanceOrmMapperService>();

        return services;
    }
}
