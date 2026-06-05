import { FormEvent, useEffect, useMemo, useState } from 'react';
import { Pencil, Plus, RefreshCw, Trash2 } from 'lucide-react';
import { Button, EmptyState, Input, Select, StatusBadge, Table, type TableColumn } from '../components/ui';
import { displayEnum } from '../lib/display';
import { formatMoney, toApiDate } from '../lib/financeFormat';
import { useAccounts } from '../services/accountsService';
import { createRecurringRule, deleteRecurringRule, getSubscriptionStatus, matchRecurringRules, updateRecurringRule } from '../services/organizationService';
import type { RecurringFrequency, RecurringRuleDto, SubscriptionStatusDto, TransactionClassification, UpsertRecurringRuleRequest } from '../types/schema';

const frequencies: RecurringFrequency[] = ['daily', 'weekly', 'biweekly', 'monthly', 'quarterly', 'yearly'];
const classifications: TransactionClassification[] = ['personal', 'business', 'mixed', 'ignored', 'unknown'];
const emptyRule = (): UpsertRecurringRuleRequest => ({
  name: '', accountId: null, type: 'expense', classification: 'personal', amount: 0, currency: 'USD',
  category: '', merchantKeyword: '', frequency: 'monthly', nextExpected: toApiDate(new Date()), amountTolerance: 0.2, tags: [], isActive: true,
});

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
  return <div className="panel"><div className="panel-header"><h2>{title}</h2><span>{rules.length}</span></div>{rules.length === 0 ? <EmptyState title={empty} /> : <Table data={rules} columns={columns} rowKey="id" />}</div>;
}
