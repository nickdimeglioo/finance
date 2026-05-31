using FinanceTracker.Api.Authentication;
using FinanceTracker.Api.Configuration;
using FinanceTracker.Api.Infrastructure.Data;
using FinanceTracker.Api.Infrastructure.Data.SqlMapper;
using FinanceTracker.Api.Infrastructure.Storage;
using FinanceTracker.Api.Services;
using FinanceTracker.Data.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

var databaseOptions = builder.Configuration
    .GetSection(DatabaseOptions.SectionName)
    .Get<DatabaseOptions>() ?? new DatabaseOptions();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, ".data-protection-keys")));

builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<ObjectStorageOptions>(builder.Configuration.GetSection(ObjectStorageOptions.SectionName));
builder.Services.Configure<FinanceAuthOptions>(builder.Configuration.GetSection(FinanceAuthOptions.SectionName));

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<NpgsqlConnectionFactory>();
builder.Services.AddFinanceOrmMapper(databaseOptions.ConnectionString);
builder.Services.AddScoped<IFinanceDataSession, FinanceSqlMapperDataSession>();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddScoped<IObjectStorageService, S3CompatibleObjectStorageService>();
builder.Services.AddScoped<HealthDiagnosticService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<TransferService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<GuidanceService>();
builder.Services.AddScoped<DashboardService>();

builder.Services
    .AddAuthentication(DevelopmentAuthHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthHandler>(DevelopmentAuthHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalFinanceClient", policy =>
    {
        policy.WithOrigins("http://localhost:5273")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("LocalFinanceClient");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
