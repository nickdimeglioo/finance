using FinanceTracker.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using PipelineRunner.Services;

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

        services.AddScoped<IExecutionContext, FinanceExecutionContext>();
        services.AddScoped<IConnectionProvider>(provider =>
            new DefaultConnectionProvider(
                connectionString,
                provider.GetService<IExecutionContext>()));
        services.AddScoped<IOrmMapperService, OrmMapperService>();

        return services;
    }
}

public sealed class FinanceExecutionContext : IExecutionContext
{
    private readonly ICurrentUserContext _currentUser;
    private Guid? _organizationId;
    private Guid? _projectId;
    private Guid? _userId;

    public FinanceExecutionContext(ICurrentUserContext currentUser)
    {
        _currentUser = currentUser;
    }

    public Guid? OrganizationId
    {
        get => _organizationId;
        set => _organizationId = value;
    }

    public Guid? ProjectId
    {
        get => _projectId;
        set => _projectId = value;
    }

    public Guid? UserId
    {
        get => _userId ?? TryGetCurrentUserId();
        set => _userId = value;
    }

    public void Set(Guid? orgId, Guid? projectId, Guid? userId)
    {
        _organizationId = orgId;
        _projectId = projectId;
        _userId = userId;
    }

    public void Clear()
    {
        _organizationId = null;
        _projectId = null;
        _userId = null;
    }

    private Guid? TryGetCurrentUserId()
    {
        try
        {
            return _currentUser.UserId;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
