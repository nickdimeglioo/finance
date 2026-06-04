import { FormEvent, useEffect, useMemo, useState } from 'react';
import { ArrowLeft, Check, ChevronLeft, ChevronRight, ClipboardList, Download, Plus, Save, Trash2, Upload } from 'lucide-react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { Button, Checkbox, EmptyState, Input, Select, StatusBadge, Table, type TableColumn } from '../components/ui';
import { displayEnum } from '../lib/display';
import { buildRulesetPrompt } from '../lib/rulesetPrompt';
import { useAccounts } from '../services/accountsService';
import {
  deleteRuleset,
  exportRuleset,
  getRuleset,
  previewRulesetImport,
  runRulesetImport,
  updateRuleset,
} from '../services/importsService';
import type {
  ClassificationConditionDto,
  RulesetDto,
  RulesetImportRowOverride,
  RulesetImportPreviewRowDto,
  RulesetImportResult,
  RulesetRuleDefinitionDto,
  RulesetRuleOutputDto,
  TransactionClassification,
} from '../types/schema';

const classifications: TransactionClassification[] = ['personal', 'business', 'mixed', 'ignored', 'unknown'];
const fieldTargets = ['date', 'amount', 'type', 'isDebit', 'isCredit', 'uniqueId', 'description', 'merchant', 'category', 'subcategory', 'classification', 'tags'];
const conditionOps = ['contains', 'notContains', 'equals', 'startsWith', 'endsWith', 'regex', 'in', 'isEmpty', 'isNotEmpty', 'gt', 'lt', 'gte', 'lte'];
const transformTypes = ['toString', 'trim', 'toDecimal', 'parseDate', 'toDate', 'toEnum', 'toBoolean', 'splitTags', 'toUpper', 'toLower'];
const previewPageSizes = [25, 50, 100, 0];
type PreviewStatusFilter = 'all' | 'ready' | 'excluded' | 'duplicate' | 'error';

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

export function RulesetPage() {
  const { rulesetId = '' } = useParams();
  const navigate = useNavigate();
  const { accounts } = useAccounts();
  const [accountId, setAccountId] = useState('');
  const [ruleset, setRuleset] = useState<RulesetDto | null>(null);
  const [file, setFile] = useState<File | null>(null);
  const [previewResult, setPreviewResult] = useState<RulesetImportResult | null>(null);
  const [previewSearch, setPreviewSearch] = useState('');
  const [previewStatus, setPreviewStatus] = useState<PreviewStatusFilter>('all');
  const [previewPage, setPreviewPage] = useState(1);
  const [previewPageSize, setPreviewPageSize] = useState(50);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [ruleForm, setRuleForm] = useState<RuleForm>(emptyRuleForm);
  const [draftRules, setDraftRules] = useState<RulesetRuleDefinitionDto[]>([]);

  useEffect(() => {
    reloadRuleset();
  }, [rulesetId]);

  const rules = ruleset?.rules.rules ?? [];
  const acceptedCount = useMemo(() => previewResult?.previewRows.filter((row) => row.accepted).length ?? 0, [previewResult]);
  const duplicateCount = useMemo(() => previewResult?.previewRows.filter((row) => row.isDuplicate).length ?? 0, [previewResult]);
  const errorCount = useMemo(() => previewResult?.previewRows.filter((row) => row.errors.length > 0).length ?? 0, [previewResult]);
  const filteredPreviewRows = useMemo(() => {
    const search = previewSearch.trim().toLowerCase();
    return (previewResult?.previewRows ?? []).filter((row) => {
      const matchesStatus = previewStatus === 'all'
        || (previewStatus === 'ready' && row.accepted && !row.isDuplicate && row.errors.length === 0)
        || (previewStatus === 'excluded' && !row.accepted)
        || (previewStatus === 'duplicate' && row.isDuplicate)
        || (previewStatus === 'error' && row.errors.length > 0);
      const matchesSearch = !search || [
        row.description,
        row.merchant,
        row.category,
        row.subcategory,
        row.type,
        row.classification,
        ...row.tags,
      ].some((value) => value?.toLowerCase().includes(search));
      return matchesStatus && matchesSearch;
    });
  }, [previewResult, previewSearch, previewStatus]);
  const previewPageCount = previewPageSize === 0 ? 1 : Math.max(1, Math.ceil(filteredPreviewRows.length / previewPageSize));
  const visiblePreviewRows = previewPageSize === 0
    ? filteredPreviewRows
    : filteredPreviewRows.slice((previewPage - 1) * previewPageSize, previewPage * previewPageSize);

  async function reloadRuleset() {
    if (!rulesetId) {
      return;
    }
    try {
      setRuleset(await getRuleset(rulesetId));
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to load ruleset.');
    }
  }

  async function saveRules(nextRules: RulesetRuleDefinitionDto[], successMessage?: string) {
    if (!ruleset) {
      setMessage('Ruleset is not loaded.');
      return;
    }

    setBusy(true);
    setMessage(null);
    try {
      const updated = await updateRuleset(ruleset.id, {
        name: ruleset.name,
        description: ruleset.description,
        sourceConfig: ruleset.sourceConfig,
        rules: { ...ruleset.rules, rules: nextRules },
        isActive: ruleset.isActive,
      });
      setRuleset(updated);
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
    if (!accountId || !ruleset || !file) {
      setMessage('Choose an account and CSV file.');
      return;
    }

    setBusy(true);
    setMessage(null);
    try {
      const result = commit
        ? await runRulesetImport(accountId, ruleset.id, file, {
          acceptedRowNumbers: previewResult?.previewRows.filter((row) => row.accepted).map((row) => row.rowNumber) ?? [],
          rowOverrides: previewResult?.previewRows.map(toRowOverride) ?? [],
        })
        : await previewRulesetImport(accountId, ruleset.id, file);
      setPreviewResult(result);
      setPreviewPage(1);
      setMessage(commit
        ? `${result.job.successRows} rows committed. Dashboard totals include committed rows inside the dashboard date range.`
        : null);
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
    if (!ruleset) {
      return;
    }
    await navigator.clipboard.writeText(buildRulesetPrompt(ruleset));
    setMessage('Prompt copied.');
  }

  async function downloadRuleset() {
    if (!ruleset) {
      return;
    }

    const exported = await exportRuleset(ruleset.id);
    const blob = new Blob([JSON.stringify(exported, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `${ruleset.name.toLowerCase().replace(/[^a-z0-9]+/g, '-') || 'ruleset'}.json`;
    link.click();
    URL.revokeObjectURL(url);
  }

  function updatePreviewRow(rowNumber: number, changes: Partial<RulesetImportPreviewRowDto>) {
    setPreviewResult((current) => current ? {
      ...current,
      previewRows: current.previewRows.map((row) => row.rowNumber === rowNumber ? { ...row, ...changes } : row),
    } : current);
  }

  const previewColumns: TableColumn<RulesetImportPreviewRowDto>[] = [
    {
      key: 'accepted',
      label: 'Use',
      render: (row) => (
        <Checkbox
          checked={row.accepted}
          disabled={row.isDuplicate}
          aria-label={`Use row ${row.rowNumber}`}
          onChange={(event) => updatePreviewRow(row.rowNumber, { accepted: event.target.checked })}
        />
      ),
    },
    { key: 'rowNumber', label: '#', width: 60 },
    {
      key: 'date',
      label: 'Date',
      render: (row) => <input className="table-input date-input" type="date" value={row.date ?? ''} onChange={(event) => updatePreviewRow(row.rowNumber, { date: event.target.value || null })} />,
    },
    {
      key: 'description',
      label: 'Description',
      render: (row) => <input className="table-input description-input" value={row.description ?? ''} onChange={(event) => updatePreviewRow(row.rowNumber, { description: event.target.value || null })} />,
    },
    {
      key: 'type',
      label: 'Type',
      render: (row) => (
        <select className="table-input" value={row.type ?? ''} onChange={(event) => updatePreviewRow(row.rowNumber, { type: (event.target.value || null) as RulesetImportPreviewRowDto['type'] })}>
          <option value="">Missing</option>
          {['income', 'expense', 'transfer', 'adjustment', 'opening_balance'].map((type) => <option key={type} value={type}>{displayEnum(type)}</option>)}
        </select>
      ),
    },
    {
      key: 'classification',
      label: 'Class',
      render: (row) => (
        <select className="table-input" value={row.classification ?? ''} onChange={(event) => updatePreviewRow(row.rowNumber, { classification: (event.target.value || null) as RulesetImportPreviewRowDto['classification'] })}>
          <option value="">Missing</option>
          {classifications.map((classification) => <option key={classification} value={classification}>{displayEnum(classification)}</option>)}
        </select>
      ),
    },
    {
      key: 'merchant',
      label: 'Merchant',
      render: (row) => <input className="table-input" value={row.merchant ?? ''} onChange={(event) => updatePreviewRow(row.rowNumber, { merchant: event.target.value || null })} />,
    },
    {
      key: 'category',
      label: 'Category',
      render: (row) => <input className="table-input" value={row.category ?? ''} onChange={(event) => updatePreviewRow(row.rowNumber, { category: event.target.value || null })} />,
    },
    {
      key: 'subcategory',
      label: 'Subcategory',
      render: (row) => <input className="table-input" value={row.subcategory ?? ''} onChange={(event) => updatePreviewRow(row.rowNumber, { subcategory: event.target.value || null })} />,
    },
    {
      key: 'tags',
      label: 'Tags',
      render: (row) => <input className="table-input" value={row.tags.join(', ')} onChange={(event) => updatePreviewRow(row.rowNumber, { tags: splitTags(event.target.value) })} />,
    },
    {
      key: 'amount',
      label: 'Amount',
      align: 'right',
      render: (row) => <input className="table-input amount-input" type="number" step="0.01" value={row.amount ?? ''} onChange={(event) => updatePreviewRow(row.rowNumber, { amount: event.target.value === '' ? null : Number(event.target.value) })} />,
    },
    {
      key: 'status',
      label: 'Status',
      render: (row) => (
        <span title={row.errors.map((error) => error.message).join(' ')}>
          {row.errors.length > 0
            ? <StatusBadge label="Error" tone="danger" />
            : row.isDuplicate
              ? <StatusBadge label="Duplicate" tone="warning" />
              : row.accepted
                ? <StatusBadge label="Ready" tone="success" />
                : <StatusBadge label="Excluded" tone="neutral" />}
        </span>
      ),
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
          <Link className="back-link" to="/imports"><ArrowLeft size={15} /> Rulesets</Link>
          <h1 className="page-title">{ruleset?.name ?? 'Ruleset'}</h1>
          <p className="page-subtitle">Version {ruleset?.version ?? '-'} · Upload statements, review every row, and manage this ruleset.</p>
        </div>
        <div className="row-actions">
          <Button type="button" variant="secondary" leftIcon={<ClipboardList size={15} />} onClick={copyPrompt} disabled={!ruleset}>Copy Prompt</Button>
          <Button type="button" variant="secondary" leftIcon={<Download size={15} />} onClick={downloadRuleset} disabled={!ruleset}>Export</Button>
          <Button
            type="button"
            variant="destructive"
            leftIcon={<Trash2 size={15} />}
            disabled={!ruleset || busy}
            onClick={async () => {
              if (!ruleset || !window.confirm(`Delete ${ruleset.name}? Existing imported transactions will remain.`)) {
                return;
              }
              setBusy(true);
              try {
                await deleteRuleset(ruleset.id);
                navigate('/imports');
              } finally {
                setBusy(false);
              }
            }}
          >
            Delete Ruleset
          </Button>
        </div>
      </header>

      {message && <div className="notice">{message}</div>}

      <form className="panel form-grid" onSubmit={(event) => {
        event.preventDefault();
        runPreview(false);
      }}>
        <Select label="Account" value={accountId} onChange={(event) => setAccountId(event.target.value)} required>
          <option value="">Select account</option>
          {accounts.map((account) => <option key={account.id} value={account.id}>{account.nickname}</option>)}
        </Select>
        <Input label="CSV file" type="file" accept=".csv,text/csv" onChange={(event) => setFile(event.target.files?.[0] ?? null)} required />
        <Button type="submit" loading={busy} leftIcon={<Upload size={15} />}>Preview</Button>
        <Button type="button" loading={busy} disabled={!previewResult || acceptedCount === 0 || busy} leftIcon={<Check size={15} />} onClick={() => runPreview(true)}>Commit {acceptedCount} Rows</Button>
      </form>

      <div className="panel">
        <div className="section-heading">
          <div>
            <h2>Preview</h2>
            <p>{previewResult?.previewRows.length ?? 0} total, {acceptedCount} selected, {duplicateCount} duplicates, {errorCount} errors.</p>
          </div>
          <div className="row-actions">
            <Button type="button" size="sm" variant="secondary" disabled={!previewResult} onClick={() => {
              const visible = new Set(filteredPreviewRows.filter((row) => !row.isDuplicate).map((row) => row.rowNumber));
              setPreviewResult((current) => current ? { ...current, previewRows: current.previewRows.map((row) => visible.has(row.rowNumber) ? { ...row, accepted: true } : row) } : current);
            }}>Select Filtered</Button>
            <Button type="button" size="sm" variant="secondary" disabled={!previewResult} onClick={() => {
              const visible = new Set(filteredPreviewRows.map((row) => row.rowNumber));
              setPreviewResult((current) => current ? { ...current, previewRows: current.previewRows.map((row) => visible.has(row.rowNumber) ? { ...row, accepted: false } : row) } : current);
            }}>Exclude Filtered</Button>
          </div>
        </div>
        <div className="preview-toolbar">
          <Input label="Filter rows" value={previewSearch} placeholder="Description, category, type, tag..." onChange={(event) => { setPreviewSearch(event.target.value); setPreviewPage(1); }} />
          <Select label="Status" value={previewStatus} onChange={(event) => { setPreviewStatus(event.target.value as PreviewStatusFilter); setPreviewPage(1); }}>
            <option value="all">All rows</option>
            <option value="ready">Selected and ready</option>
            <option value="excluded">Excluded</option>
            <option value="duplicate">Duplicates</option>
            <option value="error">Errors</option>
          </Select>
          <Select label="Rows per page" value={previewPageSize} onChange={(event) => { setPreviewPageSize(Number(event.target.value)); setPreviewPage(1); }}>
            {previewPageSizes.map((size) => <option key={size} value={size}>{size === 0 ? 'All' : size}</option>)}
          </Select>
        </div>
        {!previewResult || previewResult.previewRows.length === 0
          ? <EmptyState title="No preview rows" message="Choose an account and preview a CSV." />
          : filteredPreviewRows.length === 0
            ? <EmptyState title="No matching preview rows" message="Change the preview filters." />
            : <>
              <Table data={visiblePreviewRows} columns={previewColumns} rowKey={(row) => `${row.rowNumber}`} />
              {previewPageSize !== 0 && previewPageCount > 1 && (
                <div className="pagination-bar">
                  <span>Page {previewPage} of {previewPageCount} · {filteredPreviewRows.length} rows</span>
                  <div className="row-actions">
                    <Button type="button" size="sm" variant="secondary" leftIcon={<ChevronLeft size={14} />} disabled={previewPage <= 1} onClick={() => setPreviewPage((page) => Math.max(1, page - 1))}>Previous</Button>
                    <Button type="button" size="sm" variant="secondary" rightIcon={<ChevronRight size={14} />} disabled={previewPage >= previewPageCount} onClick={() => setPreviewPage((page) => Math.min(previewPageCount, page + 1))}>Next</Button>
                  </div>
                </div>
              )}
            </>}
      </div>

      <div className="panel">
        <div className="section-heading">
          <div>
            <h2>Rules</h2>
            <p>{rules.length} rules in this ruleset. Empty rulesets are allowed.</p>
          </div>
        </div>

        <div className="rule-import-bar">
          <Input
            label="Import rules JSON or CSV"
            type="file"
            accept=".json,.csv,application/json,text/csv"
            onChange={(event) => loadRuleDrafts(event.target.files?.[0] ?? null)}
            disabled={!ruleset}
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
          <Button type="submit" disabled={!ruleset} leftIcon={<Plus size={15} />}>Add Rule</Button>
        </form>

        <Table data={rules} columns={ruleColumns} rowKey={(rule) => rule.id} emptyMessage="No rules in this ruleset." />
      </div>
    </section>
  );
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

function toRowOverride(row: RulesetImportPreviewRowDto): RulesetImportRowOverride {
  return {
    rowNumber: row.rowNumber,
    date: row.date,
    amount: row.amount,
    type: row.type,
    description: row.description,
    merchant: row.merchant,
    category: row.category,
    subcategory: row.subcategory,
    classification: row.classification,
    tags: row.tags,
  };
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
