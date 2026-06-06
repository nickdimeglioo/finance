import { ChangeEvent, FormEvent, useEffect, useMemo, useState } from 'react';
import { ClipboardList, Pencil, Plus, RefreshCw, Trash2, Upload } from 'lucide-react';
import { Button, EmptyState, Input, Select, StatusBadge, Table, type TableColumn } from '../components/ui';
import { displayEnum } from '../lib/display';
import { formatMoney, toApiDate } from '../lib/financeFormat';
import { useAccounts } from '../services/accountsService';
import { createRecurringRule, deleteRecurringRule, getSubscriptionStatus, matchRecurringRules, updateRecurringRule } from '../services/organizationService';
import type { RecurringFrequency, RecurringRuleDto, SubscriptionStatusDto, TransactionClassification, UpsertRecurringRuleRequest } from '../types/schema';

const frequencies: RecurringFrequency[] = ['daily', 'weekly', 'biweekly', 'monthly', 'quarterly', 'yearly'];
const classifications: TransactionClassification[] = ['personal', 'business', 'transfer', 'investment', 'tax', 'reimbursement', 'exclude', 'mixed', 'ignored', 'unknown'];
const pageSizes = [10, 25, 50];
const emptyRule = (): UpsertRecurringRuleRequest => ({
  name: '', accountId: null, type: 'expense', classification: 'personal', amount: 0, currency: 'USD',
  category: '', merchantKeyword: '', frequency: 'monthly', nextExpected: toApiDate(new Date()), amountTolerance: 0.2, tags: [], isActive: true,
});

const subscriptionImportPrompt = `Create subscription/recurring transaction rules for this finance tracker.

Return either JSON or CSV.

JSON shape:
[
  {
    "name": "Netflix",
    "accountId": null,
    "type": "expense",
    "classification": "personal",
    "amount": 19.99,
    "currency": "USD",
    "category": "Entertainment",
    "merchantKeyword": "NETFLIX",
    "frequency": "monthly",
    "nextExpected": "2026-07-01",
    "amountTolerance": 0.2,
    "tags": ["subscription", "streaming"],
    "isActive": true
  }
]

CSV headers:
name,accountId,accountName,type,classification,amount,currency,category,merchantKeyword,frequency,nextExpected,amountTolerance,tags,isActive

Rules:
- type must be expense, income, adjustment, or transfer.
- classification must be one of: personal, business, transfer, investment, tax, reimbursement, exclude, mixed, ignored, or unknown. Prefer exclude over ignored for new non-reportable rules.
- frequency must be daily, weekly, biweekly, monthly, quarterly, or yearly.
- tags may be a comma, semicolon, or pipe-separated string.
- Use accountId when known; otherwise accountName can match an existing account nickname.`;

export function SubscriptionsPage() {
  const { accounts } = useAccounts();
  const [status, setStatus] = useState<SubscriptionStatusDto | null>(null);
  const [draft, setDraft] = useState<UpsertRecurringRuleRequest>(emptyRule);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function reload() {
    try { setStatus(await getSubscriptionStatus()); setMessage(null); } catch (err) { setMessage(err instanceof Error ? err.message : 'Unable to load subscriptions.'); }
  }
  useEffect(() => { void reload(); }, []);
  const overdue = useMemo(() => status?.rules.filter((rule) => rule.status === 'overdue') ?? [], [status]);
  const upcoming = useMemo(() => status?.rules.filter((rule) => rule.status === 'upcoming') ?? [], [status]);

  async function submit(event: FormEvent) {
    event.preventDefault(); setBusy(true);
    try {
      if (editingId) await updateRecurringRule(editingId, draft); else await createRecurringRule(draft);
      setDraft(emptyRule()); setEditingId(null); await reload();
    } catch (err) { setMessage(err instanceof Error ? err.message : 'Unable to save recurring rule.'); } finally { setBusy(false); }
  }

  async function copyPrompt() {
    await navigator.clipboard.writeText(subscriptionImportPrompt);
    setMessage('Subscription import prompt copied.');
  }

  async function importSubscriptions(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) return;

    setBusy(true);
    try {
      const text = await file.text();
      const requests = parseSubscriptionImport(text, file.name, accounts);
      for (const request of requests) {
        await createRecurringRule(request);
      }
      setMessage(`Imported ${requests.length} subscription rules.`);
      await reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to import subscription rules.');
    } finally {
      setBusy(false);
    }
  }

  function edit(rule: RecurringRuleDto) {
    setEditingId(rule.id);
    setDraft({
      name: rule.name, accountId: rule.accountId, type: rule.type, classification: rule.classification, amount: rule.amount,
      currency: rule.currency, category: rule.category, merchantKeyword: rule.merchantKeyword, frequency: rule.frequency,
      nextExpected: rule.nextExpected, amountTolerance: rule.amountTolerance, tags: rule.tags, isActive: rule.isActive,
    });
  }

  const columns: TableColumn<RecurringRuleDto>[] = [
    { key: 'name', label: 'Rule' },
    { key: 'status', label: 'Status', render: (rule) => <StatusBadge label={displayEnum(rule.status)} tone={rule.status === 'overdue' ? 'danger' : rule.status === 'upcoming' ? 'warning' : 'success'} /> },
    { key: 'nextExpected', label: 'Next expected' },
    { key: 'lastMatchedDate', label: 'Last matched', render: (rule) => rule.lastMatchedDate ?? 'Never' },
    { key: 'amount', label: 'Amount', align: 'right', render: (rule) => formatMoney(rule.amount, rule.currency) },
    { key: 'monthlyNormalizedCost', label: 'Monthly', align: 'right', render: (rule) => formatMoney(rule.monthlyNormalizedCost, rule.currency) },
    { key: 'actions', label: '', align: 'right', render: (rule) => <div className="row-actions">
      <Button type="button" size="sm" variant="secondary" leftIcon={<Pencil size={14} />} onClick={() => edit(rule)}>Edit</Button>
      <Button type="button" size="sm" variant="destructive" leftIcon={<Trash2 size={14} />} onClick={async () => { if (window.confirm(`Delete ${rule.name}?`)) { await deleteRecurringRule(rule.id); await reload(); } }}>Delete</Button>
    </div> },
  ];

  return <section className="page">
    <header className="page-header">
      <div><h1 className="page-title">Subscriptions</h1><p className="page-subtitle">Deterministic recurring transaction matching and normalized monthly spend.</p></div>
      <Button type="button" variant="secondary" leftIcon={<RefreshCw size={15} />} onClick={async () => { const result = await matchRecurringRules(); setMessage(`${result.matched} transactions matched.`); await reload(); }}>Match Transactions</Button>
    </header>
    {message && <div className="notice">{message}</div>}
    <div className="metric-grid">
      <Metric label="Monthly total" value={formatMoney(status?.monthlyTotal ?? 0)} />
      <Metric label="Business monthly" value={formatMoney(status?.businessMonthlyTotal ?? 0)} />
      <Metric label="Personal monthly" value={formatMoney(status?.personalMonthlyTotal ?? 0)} />
    </div>
    <div className="panel rule-import-bar">
      <Input label="Import subscriptions" type="file" accept=".json,.csv,application/json,text/csv" onChange={importSubscriptions} />
      <Button type="button" variant="secondary" leftIcon={<ClipboardList size={15} />} onClick={copyPrompt}>Copy Prompt</Button>
      <Button type="button" variant="secondary" leftIcon={<Upload size={15} />} onClick={() => document.querySelector<HTMLInputElement>('input[type="file"]')?.click()} disabled={busy}>Choose File</Button>
    </div>
    <form className="panel form-grid" onSubmit={submit}>
      <Input label="Rule name" value={draft.name} onChange={(event) => setDraft({ ...draft, name: event.target.value })} required />
      <Select label="Account" value={draft.accountId ?? ''} onChange={(event) => setDraft({ ...draft, accountId: event.target.value || null })}><option value="">Any account</option>{accounts.map((account) => <option key={account.id} value={account.id}>{account.nickname}</option>)}</Select>
      <Select label="Type" value={draft.type} onChange={(event) => setDraft({ ...draft, type: event.target.value })}><option value="expense">Expense</option><option value="income">Income</option><option value="adjustment">Adjustment</option><option value="transfer">Transfer</option></Select>
      <Select label="Classification" value={draft.classification} onChange={(event) => setDraft({ ...draft, classification: event.target.value as TransactionClassification })}>{classifications.map((value) => <option key={value} value={value}>{displayEnum(value)}</option>)}</Select>
      <Input label="Amount" type="number" min="0.01" step="0.01" value={draft.amount} onChange={(event) => setDraft({ ...draft, amount: Number(event.target.value) })} required />
      <Input label="Merchant keyword" value={draft.merchantKeyword ?? ''} onChange={(event) => setDraft({ ...draft, merchantKeyword: event.target.value })} />
      <Input label="Category" value={draft.category ?? ''} onChange={(event) => setDraft({ ...draft, category: event.target.value })} />
      <Select label="Frequency" value={draft.frequency} onChange={(event) => setDraft({ ...draft, frequency: event.target.value as RecurringFrequency })}>{frequencies.map((value) => <option key={value} value={value}>{displayEnum(value)}</option>)}</Select>
      <Input label="Next expected" type="date" value={draft.nextExpected} onChange={(event) => setDraft({ ...draft, nextExpected: event.target.value })} />
      <Input label="Tolerance" type="number" min="0" max="1" step="0.01" helperText="0.20 means ±20%." value={draft.amountTolerance} onChange={(event) => setDraft({ ...draft, amountTolerance: Number(event.target.value) })} />
      <Input label="Tags" value={(draft.tags ?? []).join(', ')} onChange={(event) => setDraft({ ...draft, tags: event.target.value.split(',').map((value) => value.trim()).filter(Boolean) })} />
      <Button type="submit" loading={busy} leftIcon={editingId ? <Pencil size={14} /> : <Plus size={14} />}>{editingId ? 'Update Rule' : 'Add Rule'}</Button>
      {editingId && <Button type="button" variant="secondary" onClick={() => { setEditingId(null); setDraft(emptyRule()); }}>Cancel</Button>}
    </form>
    <RuleSection title="Overdue" rules={overdue} columns={columns} empty="No overdue recurring rules." />
    <RuleSection title="Due This Month" rules={upcoming} columns={columns} empty="No recurring rules due this month." />
    <RuleSection title="All Active Rules" rules={status?.rules ?? []} columns={columns} empty="No recurring rules yet." />
  </section>;
}

function Metric({ label, value }: { label: string; value: string }) {
  return <div className="metric"><div className="metric-label">{label}</div><div className="metric-value">{value}</div></div>;
}

function RuleSection({ title, rules, columns, empty }: { title: string; rules: RecurringRuleDto[]; columns: TableColumn<RecurringRuleDto>[]; empty: string }) {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const totalPages = Math.max(1, Math.ceil(rules.length / pageSize));
  const safePage = Math.min(page, totalPages);
  const pageRows = rules.slice((safePage - 1) * pageSize, safePage * pageSize);

  useEffect(() => {
    setPage(1);
  }, [rules, pageSize]);

  return <div className="panel">
    <div className="panel-header"><h2>{title}</h2><span>{rules.length}</span></div>
    {rules.length === 0 ? <EmptyState title={empty} /> : <Table data={pageRows} columns={columns} rowKey="id" />}
    {rules.length > 0 && <div className="pagination-bar">
      <span>Page {safePage} of {totalPages}</span>
      <div className="pagination-controls">
        <Select aria-label={`${title} rows per page`} value={pageSize} onChange={(event) => setPageSize(Number(event.target.value))}>
          {pageSizes.map((size) => <option key={size} value={size}>{size} per page</option>)}
        </Select>
        <Button type="button" variant="secondary" size="sm" onClick={() => setPage((current) => Math.max(1, current - 1))} disabled={safePage <= 1}>Prev</Button>
        <Button type="button" variant="secondary" size="sm" onClick={() => setPage((current) => Math.min(totalPages, current + 1))} disabled={safePage >= totalPages}>Next</Button>
      </div>
    </div>}
  </div>;
}

function parseSubscriptionImport(text: string, fileName: string, accounts: { id: string; nickname: string }[]): UpsertRecurringRuleRequest[] {
  const rows = fileName.toLowerCase().endsWith('.csv') ? parseCsv(text) : parseJsonRows(text);
  const requests = rows.map((row) => toRecurringRuleRequest(row, accounts));
  if (requests.length === 0) {
    throw new Error('No subscription rules were found in the file.');
  }
  return requests;
}

function parseJsonRows(text: string): Record<string, unknown>[] {
  const parsed = JSON.parse(text) as unknown;
  const rows = Array.isArray(parsed)
    ? parsed
    : isRecord(parsed) && Array.isArray(parsed.rules)
      ? parsed.rules
      : isRecord(parsed) && Array.isArray(parsed.subscriptions)
        ? parsed.subscriptions
        : null;
  if (!rows) {
    throw new Error('JSON must be an array, or an object with rules/subscriptions.');
  }
  return rows.filter(isRecord);
}

function parseCsv(text: string): Record<string, unknown>[] {
  const records = readCsvRecords(text).filter((row) => row.some((value) => value.trim().length > 0));
  if (records.length < 2) return [];
  const headers = records[0].map((header) => header.trim());
  return records.slice(1).map((record) => Object.fromEntries(headers.map((header, index) => [header, record[index] ?? ''])));
}

function readCsvRecords(text: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let field = '';
  let inQuotes = false;
  for (let index = 0; index < text.length; index += 1) {
    const char = text[index];
    if (inQuotes) {
      if (char === '"' && text[index + 1] === '"') {
        field += '"';
        index += 1;
      } else if (char === '"') {
        inQuotes = false;
      } else {
        field += char;
      }
    } else if (char === '"') {
      inQuotes = true;
    } else if (char === ',') {
      row.push(field);
      field = '';
    } else if (char === '\n') {
      row.push(field.trimEnd());
      rows.push(row);
      row = [];
      field = '';
    } else if (char !== '\r') {
      field += char;
    }
  }
  row.push(field.trimEnd());
  rows.push(row);
  return rows;
}

function toRecurringRuleRequest(row: Record<string, unknown>, accounts: { id: string; nickname: string }[]): UpsertRecurringRuleRequest {
  const name = readString(row, 'name');
  const amount = readNumber(row, 'amount');
  const frequency = readString(row, 'frequency') || 'monthly';
  const classification = readString(row, 'classification') || 'personal';
  const type = readString(row, 'type') || 'expense';
  if (!name) throw new Error('Each subscription rule needs a name.');
  if (!amount || amount <= 0) throw new Error(`${name} needs a positive amount.`);
  if (!frequencies.includes(frequency as RecurringFrequency)) throw new Error(`${name} has an invalid frequency.`);
  if (!classifications.includes(classification as TransactionClassification)) throw new Error(`${name} has an invalid classification.`);

  const accountId = readString(row, 'accountId') || accountIdFromName(readString(row, 'accountName'), accounts);
  return {
    name,
    accountId: accountId || null,
    type,
    classification: classification as TransactionClassification,
    amount,
    currency: readString(row, 'currency') || 'USD',
    category: readString(row, 'category') || null,
    merchantKeyword: readString(row, 'merchantKeyword') || null,
    frequency: frequency as RecurringFrequency,
    nextExpected: readString(row, 'nextExpected') || toApiDate(new Date()),
    amountTolerance: readNumber(row, 'amountTolerance') ?? 0.2,
    tags: readTags(readRaw(row, 'tags')),
    isActive: readBoolean(readRaw(row, 'isActive'), true),
  };
}

function readRaw(row: Record<string, unknown>, key: string): unknown {
  return Object.entries(row).find(([name]) => name.toLowerCase() === key.toLowerCase())?.[1];
}

function readString(row: Record<string, unknown>, key: string): string {
  const value = readRaw(row, key);
  return typeof value === 'string' ? value.trim() : value === null || value === undefined ? '' : String(value).trim();
}

function readNumber(row: Record<string, unknown>, key: string): number | null {
  const value = readString(row, key).replace(/[$,]/g, '');
  if (!value) return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function readTags(value: unknown): string[] {
  if (Array.isArray(value)) return value.map((tag) => String(tag).trim()).filter(Boolean);
  if (typeof value !== 'string') return [];
  return value.split(/[;,|]/).map((tag) => tag.trim()).filter(Boolean);
}

function readBoolean(value: unknown, fallback: boolean): boolean {
  if (typeof value === 'boolean') return value;
  if (typeof value !== 'string') return fallback;
  return !['false', '0', 'no', 'n'].includes(value.trim().toLowerCase());
}

function accountIdFromName(name: string, accounts: { id: string; nickname: string }[]): string | null {
  if (!name) return null;
  return accounts.find((account) => account.nickname.localeCompare(name, undefined, { sensitivity: 'accent' }) === 0)?.id ?? null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value);
}
