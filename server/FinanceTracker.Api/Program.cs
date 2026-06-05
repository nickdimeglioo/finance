using Dapper;
using FinanceTracker.Api.Authentication;
using FinanceTracker.Api.Configuration;
using FinanceTracker.Api.Infrastructure.Data;
using FinanceTracker.Api.Infrastructure.Data.SqlMapper;
using FinanceTracker.Api.Infrastructure.Storage;
using FinanceTracker.Api.Services;
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
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddScoped<IObjectStorageService>(services =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ObjectStorageOptions>>().Value;
    return string.Equals(options.Provider, "S3", StringComparison.OrdinalIgnoreCase)
        ? ActivatorUtilities.CreateInstance<S3CompatibleObjectStorageService>(services)
        : ActivatorUtilities.CreateInstance<LocalObjectStorageService>(services);
});
builder.Services.AddScoped<HealthDiagnosticService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<TransferService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<ImportRuleService>();
builder.Services.AddScoped<ClassificationRuleService>();
builder.Services.AddScoped<RulesetService>();
builder.Services.AddScoped<CsvParserService>();
builder.Services.AddScoped<MappingEngine>();
builder.Services.AddScoped<RulesetClassificationEngine>();
builder.Services.AddScoped<DeduplicationService>();
builder.Services.AddScoped<ImportOrchestrator>();
builder.Services.AddScoped<StorageFileService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<GuidanceService>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<ReconciliationService>();
builder.Services.AddScoped<ExportService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<TagService>();
builder.Services.AddScoped<RecurringRuleService>();
builder.Services.AddScoped<NoteService>();
builder.Services.AddScoped<ReminderService>();
builder.Services.AddHostedService<ReminderWorker>();

OrmConfiguration.AddType<Enum>(new EnumHandler());
OrmConfiguration.AddType<Dictionary<string, object>>(new DBSerializer());
OrmConfiguration.AddType<Dictionary<string, string>>(new DBSerializer());
OrmConfiguration.AddType<DateTimeOffset>(new DateTimeOffsetHandler());
OrmConfiguration.AddType<DateTimeOffset?>(new DateTimeOffsetHandler());
OrmConfiguration.AddType<DateOnly>(new DateOnlyHandler());
OrmConfiguration.AddType<DateOnly?>(new DateOnlyHandler());
SqlMapper.AddTypeHandler(new DateOnlyDapperTypeHandler());

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
