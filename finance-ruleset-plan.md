# Finance Importer — Ruleset System Plan (C# Backend)

## Context
Building a finance tracker with a CSV import system. Imports are driven by **rulesets** — one ruleset per bank/account type. Each ruleset has two rule categories:

1. **Mapping rules** — transform raw CSV columns into normalized transaction fields
2. **Classification rules** — categorize transactions by matching against description patterns

---

## Data Models (C# / EF Core)

### Ruleset
```csharp
Ruleset {
  Id, Name, Description, Version (int),
  SourceConfig (json),   // delimiter, encoding, expectedColumns[]
  MappingRules (json),   // ordered list of field mapping definitions
  ClassificationRules (json), // priority-ordered match/output rules
  CreatedAt, UpdatedAt, IsActive
}
```

### Transaction (stored output)
```csharp
Transaction {
  Id, Date, Amount (decimal), Type (enum: Expense|Receivable),
  UniqueId (string, unique index),  // computed composite key
  Description (string),             // raw cleaned description
  Merchant (string?),               // from classification
  Category (string?), Subcategory (string?),
  IsDuplicate (bool), ImportedAt,
  RulesetId (FK), RulesetVersion (int),
  MatchedClassificationRuleId (string?),  // null = fell through to fallback
  RawRow (json)                     // original CSV row for audit/replay
}
```

### ImportJob
```csharp
ImportJob {
  Id, RulesetId (FK), FileName,
  Status (enum: Pending|Processing|Complete|Failed),
  TotalRows, SuccessRows, SkippedRows, ErrorRows,
  Errors (json),    // [{ row, column, message }]
  IsDryRun (bool),
  StartedAt, CompletedAt
}
```

---

## Mapping Rule JSON Schema

Three levels of power — most rulesets only need levels 0–1:

```json
{
  "onError": "skip",
  "fields": [
    // Level 0 — direct column reference
    { "target": "description", "source": "Description" },

    // Level 1 — built-in transform
    { "target": "date", "source": "Date",
      "transform": { "type": "parseDate", "format": "M/D/YYYY" } },

    // Level 2 — expression (computed field)
    { "target": "amount",  "expr": "COALESCE(Credit,0) - COALESCE(Debit,0)" },
    { "target": "type",    "expr": "IF(Credit > 0, 'receivable', 'expense')" },
    { "target": "uniqueId","expr": "CONCAT(BankRTN, '-', AccountNumber, '-', Date)" }
  ]
}
```

**Built-in transforms to implement:** `parseDate`, `toDecimal`, `toString`, `trim`, `toUpper`, `toEnum`

**Expression functions to support:** `COALESCE`, `IF`, `CONCAT`, `ABS`, `ROUND`, `TRIM`, `UPPER`, `LOWER`

**Recommended library:** Use `Jint` (JS engine in .NET) or `NCalc` for expression evaluation — do NOT use Roslyn/eval for security reasons.

---

## Classification Rule JSON Schema

Priority-ordered, first match wins. Condition tree uses AND/OR nesting:

```json
{
  "fallback": { "merchant": null, "category": "Uncategorized" },
  "rules": [
    {
      "id": "starbucks",
      "priority": 100,
      "match": {
        "op": "OR",
        "conditions": [
          { "field": "description", "op": "contains",  "value": "STARBUCKS" },
          { "field": "description", "op": "regex",     "value": "SBX\\s?#\\d+" }
        ]
      },
      "output": {
        "merchant": "Starbucks",
        "category": "Food and Drink",
        "subcategory": "Coffee"
      }
    }
  ]
}
```

**Condition operators:** `contains`, `startsWith`, `endsWith`, `equals`, `regex`, `in`, `notContains`, `isEmpty`, `gt`, `lt`, `gte`, `lte`

Fields available for classification matching: `description`, `amount`, `type`, `date`

---

## Backend — C# Responsibilities

### Services to build

**`CsvParserService`**
- Accept `IFormFile` + `Ruleset`
- Validate headers against `expectedColumns`
- Normalize column name refs (e.g. "Bank RTN" → `BankRTN`)
- Return `ParsedRow[]` with error collection

**`MappingEngine`**
- Walk `MappingRules` fields in order
- Level 0: direct index lookup
- Level 1: call appropriate `ITransform` implementation
- Level 2: evaluate expression via sandboxed evaluator (NCalc/Jint)
- Return `MappedTransaction` or per-row error with `onError` policy applied

**`ClassificationEngine`**
- Sort rules by priority descending
- For each `MappedTransaction`, walk rules and evaluate condition tree recursively
- First match → apply output fields; no match → apply fallback
- Record `matchedClassificationRuleId` for audit

**`DeduplicationService`**
- Check `uniqueId` against existing transactions
- Strategy per import: `Skip` (recommended default), `Update`, `Fail`

**`ImportOrchestrator`**
- Orchestrates: Parse → Map → Classify → Deduplicate → Persist
- Creates and updates `ImportJob` record throughout
- Supports `isDryRun` flag (runs full pipeline, returns preview, does NOT write transactions)

**`RulesetImportExportService`**
- Serialize/deserialize ruleset to/from JSON for sharing
- Validate ruleset JSON on import before saving

---

## API Endpoints

```
# Rulesets
GET    /api/rulesets
POST   /api/rulesets
GET    /api/rulesets/{id}
PUT    /api/rulesets/{id}
DELETE /api/rulesets/{id}
POST   /api/rulesets/import-json    // import from shared ruleset file
GET    /api/rulesets/{id}/export    // download ruleset as JSON

# Import
POST   /api/import/preview          // dry run, returns first 20 rows mapped+classified
POST   /api/import/run              // full import
GET    /api/import/jobs/{jobId}     // poll status

# Transactions
GET    /api/transactions            // with filters: dateRange, category, type, merchant
PATCH  /api/transactions/{id}       // manual override (merchant, category)
POST   /api/transactions/{id}/create-rule  // suggest classification rule from override
```

---

## Frontend Responsibilities

**Ruleset list page** — CRUD table, import JSON button, clone existing ruleset

**Ruleset editor** (two tabs: Mapping / Classification)
- Mapping tab: table of target fields with source column picker + transform selector; expression input for computed fields; drag-to-reorder
- Classification tab: priority-ordered rule list; each rule has a condition builder (field + op + value, AND/OR groups) and output fields; drag-to-reorder priority

**Import flow (3 steps)**
1. Upload CSV + select ruleset
2. Preview (dry run) — table of first 20 rows with mapped + classified columns, error count badge
3. Confirm → show progress via job polling → summary (imported / skipped / errors)

**Transaction list** — filterable by date, category, merchant, type; inline override for merchant/category with "create rule from this?" prompt

---

## Key Implementation Notes

- Store `MappingRules` and `ClassificationRules` as `jsonb` columns (Postgres) or `nvarchar(max)` with JSON (SQL Server) — deserialize at runtime
- Add a **description normalization** step between Parse and Classification: strip trailing transaction IDs, dates, and location suffixes before matching
- Log every import to `ImportJob` including dry runs — useful for debugging ruleset issues
- Ruleset versioning: bump `Version` on every save; `Transaction.RulesetVersion` locks in which version produced that record — never silently re-classify existing data
- `uniqueId` is your idempotency key — composite of RTN + AccountNumber + Date at minimum; add CheckNumber or Amount if the bank can have same-day duplicate amounts