namespace FinanceTracker.Api.Features.Imports;

[TableName("import_templates")]
public sealed class ImportTemplate
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public string? Institution { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ColumnMap { get; set; } = "{}";
    public string? DateFormat { get; set; }
    public string? AmountFormat { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
