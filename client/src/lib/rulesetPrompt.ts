import type { RulesetDto } from '../types/schema';

const rulesetPrompt = `Create or improve a finance tracker ruleset. Return either a JSON rules array or CSV rows that can be imported into the current ruleset.

RULESET MODEL
- A ruleset has sourceConfig plus one ordered rules array.
- Rule kinds:
  - field: maps raw statement data into a transaction field.
  - enrichment: classifies or enriches an already mapped transaction.
- Rules run by ascending priority, then name, then id.
- Every rule requires: id, kind, priority, isActive.
- Optional rule properties: name, target, match, flow, output.

FIELD RULES
- Supported targets: date, amount, type, isDebit, isCredit, uniqueId, description, merchant, category, subcategory, classification, tags.
- A field rule requires target and at least one flow step.
- The first flow step whose when condition matches is used.
- Flow step inputs: source (raw CSV column), expr (expression), or value (constant).
- Flow step transforms:
  - parseDate or toDate: parse a date; optional .NET-style format such as M/d/yyyy, MM/dd/yyyy, yyyy-MM-dd.
  - toDecimal: parse numbers including commas, dollar signs, negatives, and parenthesized negatives.
  - toString, trim, toUpper, toLower.
  - toEnum: lowercase the input, or use transform.value as the fixed enum value.
  - toBoolean: false for false, 0, no, or n; true for other non-empty values.
  - splitTags: split comma, semicolon, or pipe-separated tags.
- Supported expression functions: COALESCE, IF, CONCAT, ABS, ROUND, TRIM, UPPER, LOWER.
- Expressions support +, -, >, <, >=, <=, ==, !=, quoted strings, numbers, and raw column names.
- Valid transaction types: income, expense, transfer, adjustment, opening_balance.

ENRICHMENT / CLASSIFICATION RULES
- Enrichment rules use match plus output and may update merchant, category, subcategory, classification, and tags.
- Valid classifications: business, personal, mixed, ignored, unknown.
- All matching enrichment rules run, so later rules can add tags or override earlier outputs.
- Fallback values apply after enrichment rules.

CONDITIONS
- A condition has op, field, value, and optional conditions.
- Operators: contains, notContains, equals, startsWith, endsWith, regex, in, isEmpty, isNotEmpty, gt, lt, gte, lte, AND, OR.
- Read mapped fields directly (for example description or amount), as mapped.description, or raw statement columns as row["Debit"] or row.Debit.
- AND and OR contain nested conditions.
- The in operator requires an array value.

JSON EXAMPLES
[
  {
    "id": "statement-date",
    "name": "Parse statement date",
    "kind": "field",
    "priority": 10,
    "isActive": true,
    "target": "date",
    "flow": [
      { "source": "Transaction Date", "transform": { "type": "parseDate", "format": "M/d/yyyy" } }
    ]
  },
  {
    "id": "amount-debit-credit",
    "name": "Amount from debit or credit",
    "kind": "field",
    "priority": 30,
    "isActive": true,
    "target": "amount",
    "flow": [
      { "when": { "op": "isNotEmpty", "field": "row[\\"Debit\\"]" }, "source": "Debit", "transform": { "type": "toDecimal" } },
      { "when": { "op": "isNotEmpty", "field": "row[\\"Credit\\"]" }, "source": "Credit", "transform": { "type": "toDecimal" } }
    ]
  },
  {
    "id": "restaurant-personal",
    "name": "Personal restaurant spending",
    "kind": "enrichment",
    "priority": 100,
    "isActive": true,
    "match": {
      "op": "AND",
      "conditions": [
        { "op": "contains", "field": "description", "value": "RESTAURANT" },
        { "op": "gte", "field": "amount", "value": 10 }
      ]
    },
    "output": { "category": "Meals", "classification": "personal", "tags": ["food", "card"] }
  }
]

CSV HEADERS
kind,id,name,priority,isActive,target,matchField,matchOp,matchValue,source,expr,value,transformType,transformFormat,transformValue,outputMerchant,outputCategory,outputSubcategory,outputClassification,outputTags

CSV can represent one simple condition and one field flow step per rule. Use JSON for nested conditions or multiple flow steps.`;

export function buildRulesetPrompt(ruleset: RulesetDto): string {
  return `${rulesetPrompt}

CURRENT RULESET
Use this as context. Preserve useful existing rules and return only the rules array or CSV rows to import.

${JSON.stringify(ruleset, null, 2)}`;
}
