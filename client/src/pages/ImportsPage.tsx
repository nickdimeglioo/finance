import { FormEvent, useEffect, useMemo, useState } from 'react';
import { Check, ClipboardList, Download, Plus, Save, Trash2, Upload } from 'lucide-react';
import { Button, Checkbox, EmptyState, Input, Select, StatusBadge, Table, type TableColumn } from '../components/ui';
import { displayEnum } from '../lib/display';
import { formatDate, formatMoney } from '../lib/financeFormat';
import { useAccounts } from '../services/accountsService';
import {
  createRuleset,
  deleteRuleset,
  exportRuleset,
  listRulesets,
  previewRulesetImport,
  runRulesetImport,
  updateRuleset,
} from '../services/importsService';
import type {
  ClassificationConditionDto,
  RulesetDto,
  RulesetImportPreviewRowDto,
  RulesetImportResult,
  RulesetRuleDefinitionDto,
  RulesetRuleOutputDto,
  RulesetRulesDocumentDto,
  TransactionClassification,
} from '../types/schema';

const classifications: TransactionClassification[] = ['personal', 'business', 'mixed', 'ignored', 'unknown'];
const fieldTargets = ['date', 'amount', 'type', 'isDebit', 'isCredit', 'uniqueId', 'description', 'merchant', 'category', 'subcategory', 'classification', 'tags'];
const conditionOps = ['contains', 'equals', 'startsWith', 'endsWith', 'regex', 'isEmpty', 'isNotEmpty', 'gt', 'lt', 'gte', 'lte'];
const transformTypes = ['toString', 'trim', 'toDecimal', 'parseDate', 'toEnum', 'toBoolean', 'splitTags', 'toUpper', 'toLower'];

const rulesetPrompt = `Create ruleset rules as JSON or CSV for this finance tracker.

Rules are saved into the currently selected ruleset. Use one combined rules array:
- kind: field or enrichment.
- field rules set one target column: date, amount, type, isDebit, isCredit, uniqueId, description, merchant, category, subcategory, classification, tags.
- field rules use flow steps. Each step can have when, source, expr, value, and transform.
- enrichment rules use match plus output. Output can set merchant, category, subcategory, classification, and tags.
- tags is an array of tag names and a row can have many tags.
- match/when supports op, field, value, and nested conditions. Fields can read mapped values like amount or raw CSV columns like row["Debit"].

TD Debit/Credit example:
[
  { "id": "amount-td", "name": "Amount from Debit/Credit", "kind": "field", "priority": 30, "isActive": true, "target": "amount", "flow": [
    { "when": { "op": "isNotEmpty", "field": "row[\\"Debit\\"]" }, "source": "Debit", "transform": { "type": "toDecimal" } },
    { "when": { "op": "isNotEmpty", "field": "row[\\"Credit\\"]" }, "source": "Credit", "transform": { "type": "toDecimal" } }
  ] },
  { "id": "td-food", "name": "Food tag", "kind": "enrichment", "priority": 100, "isActive": true,
    "match": { "op": "contains", "field": "description", "value": "RESTAURANT" },
    "output": { "category": "Meals", "classification": "personal", "tags": ["food", "card"] } }
]

CSV headers can be:
kind,id,name,priority,isActive,target,matchField,matchOp,matchValue,source,expr,value,transformType,transformFormat,transformValue,outputMerchant,outputCategory,outputSubcategory,outputClassification,outputTags`;

type RuleForm = {
  kind: string;
  name: string;
  target: string;
  priority: number;
  source: string;
  expr: string;
  value: string;
  transformType: string;
  transformFormat: string;
  transformValue: string;
  matchField: string;
  matchOp: string;
  matchValue: string;
  outputMerchant: string;
  outputCategory: string;
  outputSubcategory: string;
  outputClassification: string;
  outputTags: string;
  isActive: boolean;
};

const emptyRuleForm: RuleForm = {
  kind: 'field',
  name: '',
  target: 'description',
  priority: 100,
  source: '',
  expr: '',
  value: '',
  transformType: '',
  transformFormat: '',
  transformValue: '',
  matchField: '',
  matchOp: 'contains',
  matchValue: '',
  outputMerchant: '',
  outputCategory: '',
  outputSubcategory: '',
  outputClassification: '',
  outputTags: '',
  isActive: true,
};

export function ImportsPage() {
  const { accounts } = useAccounts();
  const [accountId, setAccountId] = useState('');
  const [rulesets, setRulesets] = useState<RulesetDto[]>([]);
  const [selectedRulesetId, setSelectedRulesetId] = useState('');
  const [newRulesetName, setNewRulesetName] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [previewResult, setPreviewResult] = useState<RulesetImportResult | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [ruleForm, setRuleForm] = useState<RuleForm>(emptyRuleForm);
  const [draftRules, setDraftRules] = useState<RulesetRuleDefinitionDto[]>([]);

  useEffect(() => {
    reloadRulesets();
  }, []);

  const selectedRuleset = useMemo(
    () => rulesets.find((ruleset) => ruleset.id === selectedRulesetId) ?? null,
    [rulesets, selectedRulesetId],
  );
  const rules = selectedRuleset?.rules.rules ?? [];
  const acceptedCount = useMemo(() => previewResult?.previewRows.filter((row) => row.accepted).length ?? 0, [previewResult]);
  const duplicateCount = useMemo(() => previewResult?.previewRows.filter((row) => row.isDuplicate).length ?? 0, [previewResult]);
  const errorCount = useMemo(() => previewResult?.previewRows.filter((row) => row.errors.length > 0).length ?? 0, [previewResult]);

  async function reloadRulesets(selectId?: string) {
    const loaded = await listRulesets();
    setRulesets(loaded);
    setSelectedRulesetId((current) => selectId ?? (current || (loaded[0]?.id ?? '')));
  }

  async function submitRuleset(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setMessage(null);
    try {
      const created = await createRuleset({
        name: newRulesetName,
        description: null,
        sourceConfig: { delimiter: ',', encoding: 'utf-8', expectedColumns: [] },
        rules: createTdRulesDocument(),
        isActive: true,
      });
      setNewRulesetName('');
      await reloadRulesets(created.id);
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to create ruleset.');
    } finally {
      setBusy(false);
    }
  }

  async function saveRules(nextRules: RulesetRuleDefinitionDto[], successMessage?: string) {
    if (!selectedRuleset) {
      setMessage('Select a ruleset first.');
      return;
    }

    setBusy(true);
    setMessage(null);
    try {
      const updated = await updateRuleset(selectedRuleset.id, {
        name: selectedRuleset.name,
        description: selectedRuleset.description,
        sourceConfig: selectedRuleset.sourceConfig,
        rules: { ...selectedRuleset.rules, rules: nextRules },
        isActive: selectedRuleset.isActive,
      });
      setRulesets((items) => items.map((item) => item.id === updated.id ? updated : item));
      setSelectedRulesetId(updated.id);
      if (successMessage) {
        setMessage(successMessage);
      }
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to save rules.');
    } finally {
      setBusy(false);
    }
  }

  async function submitRule(event: FormEvent) {
    event.preventDefault();
    await saveRules([...rules, ruleFromForm(ruleForm)], 'Rule added to current ruleset.');
    setRuleForm(emptyRuleForm);
  }

  async function removeRule(ruleId: string) {
    await saveRules(rules.filter((rule) => rule.id !== ruleId));
  }

  async function runPreview(commit: boolean) {
    if (!accountId || !selectedRulesetId || !file) {
      setMessage('Choose an account, ruleset, and CSV file.');
      return;
    }

    setBusy(true);
    setMessage(null);
    try {
      const result = commit
        ? await runRulesetImport(accountId, selectedRulesetId, file)
        : await previewRulesetImport(accountId, selectedRulesetId, file);
      setPreviewResult(result);
    } catch (err) {
      setMessage(err instanceof Error ? err.message : commit ? 'Unable to commit import.' : 'Unable to preview import.');
    } finally {
      setBusy(false);
    }
  }

  async function loadRuleDrafts(file: File | null) {
    if (!file) {
      return;
    }

    try {
      setDraftRules(await parseRulesFile(file));
      setMessage(null);
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to read rules file.');
    }
  }

  async function saveRuleDrafts() {
    await saveRules([...rules, ...draftRules], `${draftRules.length} rules imported into current ruleset.`);
    setDraftRules([]);
  }

  async function copyPrompt() {
    await navigator.clipboard.writeText(rulesetPrompt);
    setMessage('Prompt copied.');
  }

  async function downloadRuleset() {
    if (!selectedRuleset) {
      return;
    }

    const exported = await exportRuleset(selectedRuleset.id);
    const blob = new Blob([JSON.stringify(exported, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `${selectedRuleset.name.toLowerCase().replace(/[^a-z0-9]+/g, '-') || 'ruleset'}.json`;
    link.click();
    URL.revokeObjectURL(url);
  }

  const previewColumns: TableColumn<RulesetImportPreviewRowDto>[] = [
    { key: 'accepted', label: 'Use', render: (row) => <Checkbox checked={row.accepted} readOnly /> },
    { key: 'rowNumber', label: '#', width: 60 },
    { key: 'date', label: 'Date', render: (row) => row.date ? formatDate(row.date) : 'Missing' },
    { key: 'description', label: 'Description', render: (row) => row.description ?? 'Missing' },
    { key: 'type', label: 'Type', render: (row) => displayEnum(row.type) },
    { key: 'classification', label: 'Class', render: (row) => displayEnum(row.classification) },
    { key: 'category', label: 'Category', render: (row) => row.category ?? 'Uncategorized' },
    { key: 'tags', label: 'Tags', render: (row) => renderTags(row.tags) },
    { key: 'amount', label: 'Amount', align: 'right', render: (row) => typeof row.amount === 'number' ? formatMoney(row.amount, 'USD') : 'Missing' },
    {
      key: 'status',
      label: 'Status',
      render: (row) => row.errors.length > 0
        ? <StatusBadge label="Error" tone="danger" />
        : row.isDuplicate
          ? <StatusBadge label="Duplicate" tone="warning" />
          : <StatusBadge label="Ready" tone="success" />
    },
  ];

  const ruleColumns: TableColumn<RulesetRuleDefinitionDto>[] = [
    { key: 'priority', label: 'Priority' },
    { key: 'kind', label: 'Kind', render: (rule) => displayEnum(rule.kind) },
    { key: 'name', label: 'Name', render: (rule) => rule.name || rule.id },
    { key: 'target', label: 'Target', render: (rule) => displayEnum(rule.target) },
    { key: 'match', label: 'Match', render: (rule) => describeCondition(rule.match) },
    { key: 'output', label: 'Output', render: (rule) => describeOutput(rule.output) },
    {
      key: 'actions',
      label: '',
      align: 'right',
      render: (rule) => (
        <Button type="button" variant="destructive" size="sm" leftIcon={<Trash2 size={14} />} onClick={() => removeRule(rule.id)}>Delete</Button>
      ),
    },
  ];

  return (
    <section className="page">
      <header className="page-header">
        <div>
          <h1 className="page-title">Imports</h1>
          <p className="page-subtitle">Upload CSVs through selected rulesets, preview deterministic mappings, and commit accepted rows.</p>
        </div>
        {previewResult && <StatusBadge label={`${previewResult.job.successRows} ready, ${previewResult.job.skippedRows} skipped, ${previewResult.job.errorRows} errors`} tone={previewResult.job.errorRows > 0 ? 'warning' : 'success'} />}
      </header>

      {message && <div className="notice error">{message}</div>}

      <form className="panel form-grid" onSubmit={(event) => {
        event.preventDefault();
        runPreview(false);
      }}>
        <Select label="Account" value={accountId} onChange={(event) => setAccountId(event.target.value)} required>
          <option value="">Select account</option>
          {accounts.map((account) => <option key={account.id} value={account.id}>{account.nickname}</option>)}
        </Select>
        <Select label="Ruleset" value={selectedRulesetId} onChange={(event) => setSelectedRulesetId(event.target.value)} required>
          <option value="">Select ruleset</option>
          {rulesets.map((ruleset) => <option key={ruleset.id} value={ruleset.id}>{ruleset.name}</option>)}
        </Select>
        <Input label="CSV file" type="file" accept=".csv,text/csv" onChange={(event) => setFile(event.target.files?.[0] ?? null)} required />
        <Button type="submit" loading={busy} leftIcon={<Upload size={15} />}>Preview</Button>
        <Button type="button" loading={busy} disabled={!previewResult || busy} leftIcon={<Check size={15} />} onClick={() => runPreview(true)}>Commit</Button>
      </form>

      <div className="panel">
        <div className="section-heading">
          <div>
            <h2>Preview</h2>
            <p>{acceptedCount} accepted, {duplicateCount} duplicates, {errorCount} errors.</p>
          </div>
        </div>
        {!previewResult || previewResult.previewRows.length === 0
          ? <EmptyState title="No preview rows" message="Choose a ruleset and preview a CSV." />
          : <Table data={previewResult.previewRows} columns={previewColumns} rowKey={(row) => `${row.rowNumber}`} />}
      </div>

      <div className="panel">
        <div className="section-heading">
          <div>
            <h2>Rulesets</h2>
            <p>{selectedRuleset ? `${rules.length} rules in ${selectedRuleset.name}` : 'Create or select a ruleset.'}</p>
          </div>
          <div className="row-actions">
            <Button type="button" variant="secondary" leftIcon={<ClipboardList size={15} />} onClick={copyPrompt}>Copy Prompt</Button>
            <Button type="button" variant="secondary" leftIcon={<Download size={15} />} onClick={downloadRuleset} disabled={!selectedRuleset}>Export</Button>
            <Button
              type="button"
              variant="destructive"
              leftIcon={<Trash2 size={15} />}
              onClick={async () => {
                if (selectedRuleset) {
                  await deleteRuleset(selectedRuleset.id);
                  await reloadRulesets('');
                }
              }}
              disabled={!selectedRuleset}
            >
              Deactivate
            </Button>
          </div>
        </div>

        <form className="inline-actions" onSubmit={submitRuleset}>
          <Input label="New TD-style ruleset" value={newRulesetName} onChange={(event) => setNewRulesetName(event.target.value)} required />
          <Button type="submit" loading={busy} leftIcon={<Plus size={15} />}>Add Ruleset</Button>
        </form>

        <div className="rule-import-bar">
          <Input
            label="Import rules JSON or CSV into selected ruleset"
            type="file"
            accept=".json,.csv,application/json,text/csv"
            onChange={(event) => loadRuleDrafts(event.target.files?.[0] ?? null)}
            disabled={!selectedRuleset}
          />
          <Button type="button" variant="secondary" onClick={() => setDraftRules([])} disabled={draftRules.length === 0}>Clear</Button>
          <Button type="button" leftIcon={<Save size={15} />} onClick={saveRuleDrafts} disabled={draftRules.length === 0 || busy} loading={busy}>
            Save Imported Rules
          </Button>
        </div>

        {draftRules.length > 0 && (
          <Table
            data={draftRules}
            rowKey={(rule) => rule.id}
            columns={[
              { key: 'priority', label: 'Priority' },
              { key: 'kind', label: 'Kind', render: (rule) => displayEnum(rule.kind) },
              { key: 'name', label: 'Name', render: (rule) => rule.name || rule.id },
              { key: 'target', label: 'Target', render: (rule) => displayEnum(rule.target) },
              { key: 'output', label: 'Output', render: (rule) => describeOutput(rule.output) },
            ]}
          />
        )}

        <form className="compact-form" onSubmit={submitRule}>
          <Select label="Kind" value={ruleForm.kind} onChange={(event) => setRuleForm({ ...ruleForm, kind: event.target.value })}>
            <option value="field">Field</option>
            <option value="enrichment">Enrichment</option>
          </Select>
          <Input label="Name" value={ruleForm.name} onChange={(event) => setRuleForm({ ...ruleForm, name: event.target.value })} required />
          <Input label="Priority" type="number" value={ruleForm.priority} onChange={(event) => setRuleForm({ ...ruleForm, priority: Number(event.target.value) })} />
          <Checkbox label="Active" checked={ruleForm.isActive} onChange={(event) => setRuleForm({ ...ruleForm, isActive: event.target.checked })} />
          {ruleForm.kind === 'field' && (
            <>
              <Select label="Target" value={ruleForm.target} onChange={(event) => setRuleForm({ ...ruleForm, target: event.target.value })}>
                {fieldTargets.map((target) => <option key={target} value={target}>{displayEnum(target)}</option>)}
              </Select>
              <Input label="Source column" value={ruleForm.source} onChange={(event) => setRuleForm({ ...ruleForm, source: event.target.value })} />
              <Input label="Expression" value={ruleForm.expr} onChange={(event) => setRuleForm({ ...ruleForm, expr: event.target.value })} />
              <Input label="Constant value" value={ruleForm.value} onChange={(event) => setRuleForm({ ...ruleForm, value: event.target.value })} />
              <Select label="Transform" value={ruleForm.transformType} onChange={(event) => setRuleForm({ ...ruleForm, transformType: event.target.value })}>
                <option value="">None</option>
                {transformTypes.map((transform) => <option key={transform} value={transform}>{displayEnum(transform)}</option>)}
              </Select>
              <Input label="Transform format" value={ruleForm.transformFormat} onChange={(event) => setRuleForm({ ...ruleForm, transformFormat: event.target.value })} />
            </>
          )}
          <Input label="Match field" value={ruleForm.matchField} onChange={(event) => setRuleForm({ ...ruleForm, matchField: event.target.value })} />
          <Select label="Match op" value={ruleForm.matchOp} onChange={(event) => setRuleForm({ ...ruleForm, matchOp: event.target.value })}>
            {conditionOps.map((op) => <option key={op} value={op}>{displayEnum(op)}</option>)}
          </Select>
          <Input label="Match value" value={ruleForm.matchValue} onChange={(event) => setRuleForm({ ...ruleForm, matchValue: event.target.value })} />
          {ruleForm.kind !== 'field' && (
            <>
              <Input label="Merchant" value={ruleForm.outputMerchant} onChange={(event) => setRuleForm({ ...ruleForm, outputMerchant: event.target.value })} />
              <Input label="Category" value={ruleForm.outputCategory} onChange={(event) => setRuleForm({ ...ruleForm, outputCategory: event.target.value })} />
              <Input label="Subcategory" value={ruleForm.outputSubcategory} onChange={(event) => setRuleForm({ ...ruleForm, outputSubcategory: event.target.value })} />
              <Select label="Classification" value={ruleForm.outputClassification} onChange={(event) => setRuleForm({ ...ruleForm, outputClassification: event.target.value })}>
                <option value="">No change</option>
                {classifications.map((classification) => <option key={classification} value={classification}>{displayEnum(classification)}</option>)}
              </Select>
              <Input label="Tags" value={ruleForm.outputTags} onChange={(event) => setRuleForm({ ...ruleForm, outputTags: event.target.value })} />
            </>
          )}
          <Button type="submit" disabled={!selectedRuleset} leftIcon={<Plus size={15} />}>Add Rule</Button>
        </form>

        <Table data={rules} columns={ruleColumns} rowKey={(rule) => rule.id} emptyMessage="No rules in this ruleset." />
      </div>
    </section>
  );
}

function createTdRulesDocument(): RulesetRulesDocumentDto {
  return {
    onError: 'skip',
    fallback: { category: 'Uncategorized', classification: 'unknown', tags: [] },
    rules: [
      fieldRule('date', 'Date', 10, 'Date', 'parseDate', 'M/d/yyyy'),
      fieldRule('description', 'Description', 20, 'Description', 'trim'),
      {
        id: 'amount-debit-credit',
        name: 'Amount from Debit/Credit',
        kind: 'field',
        priority: 30,
        isActive: true,
        target: 'amount',
        flow: [
          { when: { op: 'isNotEmpty', field: 'row["Debit"]' }, source: 'Debit', transform: { type: 'toDecimal' } },
          { when: { op: 'isNotEmpty', field: 'row["Credit"]' }, source: 'Credit', transform: { type: 'toDecimal' } },
          { when: { op: 'isNotEmpty', field: 'row["Amount"]' }, source: 'Amount', transform: { type: 'toDecimal' } },
        ],
      },
      { id: 'is-debit', name: 'Debit flag', kind: 'field', priority: 40, isActive: true, target: 'isDebit', flow: [{ when: { op: 'isNotEmpty', field: 'row["Debit"]' }, value: true }] },
      { id: 'is-credit', name: 'Credit flag', kind: 'field', priority: 41, isActive: true, target: 'isCredit', flow: [{ when: { op: 'isNotEmpty', field: 'row["Credit"]' }, value: true }] },
      { id: 'type-debit', name: 'Debit is expense', kind: 'field', priority: 50, isActive: true, target: 'type', match: { op: 'equals', field: 'isDebit', value: true }, flow: [{ value: 'expense', transform: { type: 'toEnum' } }] },
      { id: 'type-credit', name: 'Credit is income', kind: 'field', priority: 51, isActive: true, target: 'type', match: { op: 'equals', field: 'isCredit', value: true }, flow: [{ value: 'income', transform: { type: 'toEnum' } }] },
      { id: 'unique-id', name: 'Unique row id', kind: 'field', priority: 60, isActive: true, target: 'uniqueId', flow: [{ expr: 'CONCAT(Date, "|", Description, "|", Debit, "|", Credit)', transform: { type: 'trim' } }] },
    ],
  };
}

function fieldRule(target: string, name: string, priority: number, source: string, transformType?: string, format?: string): RulesetRuleDefinitionDto {
  return {
    id: `field-${target}`,
    name,
    kind: 'field',
    priority,
    isActive: true,
    target,
    flow: [{ source, transform: transformType ? { type: transformType, format } : null }],
  };
}

function ruleFromForm(form: RuleForm): RulesetRuleDefinitionDto {
  const match = conditionFromForm(form);
  return {
    id: crypto.randomUUID(),
    name: form.name,
    kind: form.kind,
    priority: form.priority,
    isActive: form.isActive,
    target: form.kind === 'field' ? form.target : null,
    match,
    flow: form.kind === 'field'
      ? [{
        source: form.source || null,
        expr: form.expr || null,
        value: parseLooseValue(form.value),
        transform: form.transformType ? { type: form.transformType, format: form.transformFormat || null, value: form.transformValue || null } : null,
      }]
      : null,
    output: form.kind === 'field' ? null : {
      merchant: form.outputMerchant || null,
      category: form.outputCategory || null,
      subcategory: form.outputSubcategory || null,
      classification: (form.outputClassification || null) as TransactionClassification | null,
      tags: splitTags(form.outputTags),
    },
  };
}

function conditionFromForm(form: RuleForm): ClassificationConditionDto | null {
  if (!form.matchField) {
    return null;
  }

  return {
    field: form.matchField,
    op: form.matchOp,
    value: form.matchValue ? parseLooseValue(form.matchValue) : null,
  };
}

async function parseRulesFile(file: File): Promise<RulesetRuleDefinitionDto[]> {
  const text = await file.text();
  const trimmed = text.trim();
  if (!trimmed) {
    return [];
  }

  if (file.name.toLowerCase().endsWith('.json') || trimmed.startsWith('{') || trimmed.startsWith('[')) {
    return normalizeRulesJson(JSON.parse(trimmed));
  }

  return parseCsvRules(trimmed).map(csvRuleToDefinition);
}

function normalizeRulesJson(value: unknown): RulesetRuleDefinitionDto[] {
  if (Array.isArray(value)) {
    return value.filter(isRuleDefinition);
  }

  if (isRecord(value)) {
    const ruleset = isRecord(value.ruleset) ? value.ruleset : value;
    const document = isRecord(ruleset.rules) ? ruleset.rules : ruleset;
    const rules = document.rules;
    if (Array.isArray(rules)) {
      return rules.filter(isRuleDefinition);
    }
  }

  throw new Error('Rules JSON must be an array, a ruleset export, or an object with rules.rules.');
}

function csvRuleToDefinition(row: Record<string, string>): RulesetRuleDefinitionDto {
  const kind = read(row, 'kind') || 'enrichment';
  const output: RulesetRuleOutputDto = {
    merchant: read(row, 'outputMerchant', 'merchant') || null,
    category: read(row, 'outputCategory', 'category') || null,
    subcategory: read(row, 'outputSubcategory', 'subcategory') || null,
    classification: (read(row, 'outputClassification', 'classification') || null) as TransactionClassification | null,
    tags: splitTags(read(row, 'outputTags', 'tags')),
  };

  return {
    id: read(row, 'id') || crypto.randomUUID(),
    name: read(row, 'name') || null,
    kind,
    priority: Number(read(row, 'priority')) || 100,
    isActive: read(row, 'isActive', 'active')?.toLowerCase() !== 'false',
    target: kind === 'field' ? read(row, 'target') || null : null,
    match: read(row, 'matchField') ? {
      field: read(row, 'matchField'),
      op: read(row, 'matchOp') || 'contains',
      value: parseLooseValue(read(row, 'matchValue')),
    } : null,
    flow: kind === 'field' ? [{
      source: read(row, 'source') || null,
      expr: read(row, 'expr') || null,
      value: parseLooseValue(read(row, 'value')),
      transform: read(row, 'transformType') ? {
        type: read(row, 'transformType'),
        format: read(row, 'transformFormat') || null,
        value: read(row, 'transformValue') || null,
      } : null,
    }] : null,
    output: kind === 'field' ? null : output,
  };
}

function parseCsvRules(text: string): Record<string, string>[] {
  const rows = parseCsvRows(text);
  if (rows.length < 2) {
    return [];
  }

  const headers = rows[0].map((header) => header.trim());
  return rows.slice(1)
    .filter((row) => row.some((cell) => cell.trim()))
    .map((row) => Object.fromEntries(headers.map((header, index) => [header, row[index]?.trim() ?? ''])));
}

function parseCsvRows(text: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let cell = '';
  let inQuotes = false;

  for (let index = 0; index < text.length; index += 1) {
    const char = text[index];
    const next = text[index + 1];

    if (char === '"' && inQuotes && next === '"') {
      cell += '"';
      index += 1;
      continue;
    }

    if (char === '"') {
      inQuotes = !inQuotes;
      continue;
    }

    if (char === ',' && !inQuotes) {
      row.push(cell);
      cell = '';
      continue;
    }

    if ((char === '\n' || char === '\r') && !inQuotes) {
      if (char === '\r' && next === '\n') {
        index += 1;
      }
      row.push(cell);
      rows.push(row);
      row = [];
      cell = '';
      continue;
    }

    cell += char;
  }

  row.push(cell);
  rows.push(row);
  return rows;
}

function describeCondition(condition?: ClassificationConditionDto | null) {
  if (!condition) {
    return 'Always';
  }

  return `${condition.field ?? 'field'} ${displayEnum(condition.op)} ${condition.value ?? ''}`.trim();
}

function describeOutput(output?: RulesetRuleOutputDto | null) {
  if (!output) {
    return '';
  }

  return [
    output.merchant,
    output.category,
    output.subcategory,
    output.classification ? displayEnum(output.classification) : '',
    ...(output.tags ?? []),
  ].filter(Boolean).join(' · ');
}

function renderTags(tags: string[]) {
  if (!tags.length) {
    return '';
  }

  return <div className="tag-list">{tags.map((tag) => <span key={tag} className="tag-chip">{tag}</span>)}</div>;
}

function parseLooseValue(value?: string | null): unknown {
  if (!value) {
    return null;
  }

  const trimmed = value.trim();
  if (trimmed === 'true') {
    return true;
  }

  if (trimmed === 'false') {
    return false;
  }

  const numeric = Number(trimmed);
  return Number.isFinite(numeric) && trimmed !== '' ? numeric : trimmed;
}

function splitTags(value?: string | null): string[] {
  return (value ?? '').split(/[,;|]/)
    .map((tag) => tag.trim().toLowerCase())
    .filter(Boolean)
    .filter((tag, index, tags) => tags.findIndex((item) => item.toLowerCase() === tag.toLowerCase()) === index);
}

function read(row: Record<string, string>, ...keys: string[]) {
  for (const key of keys) {
    const found = Object.entries(row).find(([candidate]) => candidate.toLowerCase() === key.toLowerCase());
    if (found?.[1]) {
      return found[1];
    }
  }

  return '';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isRuleDefinition(value: unknown): value is RulesetRuleDefinitionDto {
  return isRecord(value) && typeof value.id === 'string' && typeof value.kind === 'string';
}
