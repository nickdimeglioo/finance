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
- In expressions, + adds numbers when both sides are numeric and concatenates otherwise.
- Prefer CONCAT(...) for stable string IDs and COALESCE(...) for debit/credit fallbacks, for example CONCAT(row["Date"], "|", row["Description"], "|", COALESCE(row["Debit"], row["Credit"], "")).
- Valid transaction types: income, expense, transfer, adjustment, opening_balance.

ENRICHMENT / CLASSIFICATION RULES
- Enrichment rules use match plus output and may update merchant, category, subcategory, classification, tags, and linked-account hints.
- Valid classifications, using only these lowercase values: personal (normal personal income/expenses), business (business-related transactions), transfer (movement between own accounts), investment (brokerage, retirement, asset activity), tax (tax payments/refunds), reimbursement (paid back by someone/employer), exclude (not counted in spending reports), mixed, ignored, unknown. Prefer exclude over ignored for new non-reportable rules.
- Do not use income as a classification. Use type: income for money received, then choose a valid classification such as personal, business, tax, or reimbursement.
- To tag people or other parsed values, use output.tagFrom instead of writing one rule per person. Each extractor supports field, regex, prefix, suffix, and format. The regex should capture the value in a named group called tag/value or in the first capture group; the captured value is slugged before prefix/suffix or {value} replacement.
- For payments or movements between your own accounts, use classification: transfer plus linked-account output fields. Do not create fake classifications.
- Linked-account output fields:
  - transferTargetAccountId: exact account id when known.
  - transferTargetAccountName: exact account nickname when id is not known; preserve names exactly, including snake_case names like discover_personal.
- These fields identify the other account only. Do not try to match a specific counterpart transaction in rules.
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
  },
  {
    "id": "zelle-contact-tag",
    "name": "Zelle contact tag",
    "kind": "enrichment",
    "priority": 120,
    "isActive": true,
    "match": { "op": "regex", "field": "description", "value": "TD\\\\s+ZELLE\\\\s+(SENT|RECEIVED).*?\\\\bZelle\\\\s+(?<tag>[A-Z][A-Z .'-]+)$" },
    "output": {
      "tags": ["zelle"],
      "tagFrom": [{ "field": "description", "regex": "TD\\\\s+ZELLE\\\\s+(?:SENT|RECEIVED).*?\\\\bZelle\\\\s+(?<tag>[A-Z][A-Z .'-]+)$", "prefix": "person-" }]
    }
  },
  {
    "id": "credit-card-payment-link",
    "name": "Credit card payment to owned card",
    "kind": "enrichment",
    "priority": 130,
    "isActive": true,
    "match": { "op": "contains", "field": "description", "value": "PAYMENT TO CREDIT CARD" },
    "output": {
      "classification": "transfer",
      "category": "Credit Card Payment",
      "transferTargetAccountName": "Rewards Credit Card"
    }
  }
]

CSV HEADERS
kind,id,name,priority,isActive,target,matchField,matchOp,matchValue,source,expr,value,transformType,transformFormat,transformValue,outputMerchant,outputCategory,outputSubcategory,outputClassification,outputTags,transferTargetAccountId,transferTargetAccountName

CSV can represent one simple condition and one field flow step per rule. Use JSON for nested conditions or multiple flow steps.`;

export function buildRulesetPrompt(ruleset: RulesetDto): string {
  return `${rulesetPrompt}

CURRENT RULESET
Use this as context. Preserve useful existing rules and return only the rules array or CSV rows to import.

${JSON.stringify(ruleset, null, 2)}`;
}
