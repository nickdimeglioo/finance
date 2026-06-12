import { FormEvent, useEffect, useMemo, useState } from 'react';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import { Button, Checkbox, EmptyState, Input, Select, StatusBadge, Table, type TableColumn } from '../components/ui';
import { displayEnum } from '../lib/display';
import { formatDate, formatMoney, monthRange } from '../lib/financeFormat';
import { useAccounts } from '../services/accountsService';
import { createBudgetGoal, deleteBudgetGoal, listBudgetGoals, updateBudgetGoal } from '../services/budgetGoalService';
import { listTags } from '../services/organizationService';
import type { BudgetGoalDto, BudgetGoalKind, TagDto, TransactionClassification, UpsertBudgetGoalRequest } from '../types/schema';

const classifications: TransactionClassification[] = ['personal', 'business', 'transfer', 'investment', 'tax', 'reimbursement', 'exclude', 'mixed', 'ignored', 'unknown'];
const currentMonth = monthRange();
const emptyDraft: UpsertBudgetGoalRequest = {
  name: '',
  kind: 'budget',
  accountId: null,
  category: '',
  classification: null,
  tagNames: [],
  startsOn: currentMonth.from,
  endsOn: currentMonth.to,
  targetAmount: 0,
  currency: 'USD',
  includeSplits: true,
  isActive: true,
};

export function BudgetGoalsPage() {
  const { accounts } = useAccounts();
  const [items, setItems] = useState<BudgetGoalDto[]>([]);
  const [tags, setTags] = useState<TagDto[]>([]);
  const [filter, setFilter] = useState<'all' | BudgetGoalKind>('all');
  const [draft, setDraft] = useState<UpsertBudgetGoalRequest>(emptyDraft);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const accountNameById = useMemo(() => new Map(accounts.map((account) => [account.id, account.nickname])), [accounts]);

  useEffect(() => { listTags().then(setTags).catch((err: Error) => setMessage(err.message)); }, []);
  useEffect(() => { void reload(); }, [filter]);

  async function reload() {
    try {
      setItems(await listBudgetGoals(filter === 'all' ? undefined : filter));
      setMessage(null);
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to load budgets and goals.');
    }
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    try {
      const request = normalizeDraft(draft);
      if (editingId) await updateBudgetGoal(editingId, request); else await createBudgetGoal(request);
      setDraft(emptyDraft);
      setEditingId(null);
      await reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to save budget or goal.');
    }
  }

  const columns: TableColumn<BudgetGoalDto>[] = [
    { key: 'name', label: 'Name', render: (item) => <div><strong>{item.name}</strong><div className="muted">{displayEnum(item.kind)} · {formatDate(item.startsOn)} to {formatDate(item.endsOn)}</div></div> },
    { key: 'scope', label: 'Scope', render: (item) => scopeLabel(item, accountNameById) },
    { key: 'targetAmount', label: 'Target', align: 'right', render: (item) => formatMoney(item.targetAmount, item.currency) },
    { key: 'currentAmount', label: 'Current', align: 'right', render: (item) => formatMoney(item.currentAmount, item.currency) },
    { key: 'remainingAmount', label: 'Remaining', align: 'right', render: (item) => formatMoney(item.remainingAmount, item.currency) },
    { key: 'percentComplete', label: 'Progress', render: (item) => <Progress value={item.percentComplete} /> },
    { key: 'status', label: 'Status', render: (item) => <StatusBadge label={displayEnum(item.status)} tone={statusTone(item.status)} /> },
    { key: 'actions', label: '', align: 'right', render: (item) => <div className="row-actions">
      <Button type="button" size="sm" variant="secondary" leftIcon={<Pencil size={14} />} onClick={() => { setEditingId(item.id); setDraft({ name: item.name, kind: item.kind, accountId: item.accountId, category: item.category, classification: item.classification, tagNames: item.tagNames, startsOn: item.startsOn, endsOn: item.endsOn, targetAmount: item.targetAmount, currency: item.currency, includeSplits: item.includeSplits, isActive: item.isActive }); }}>Edit</Button>
      <Button type="button" size="sm" variant="destructive" leftIcon={<Trash2 size={14} />} onClick={async () => { if (window.confirm(`Delete ${item.name}?`)) { await deleteBudgetGoal(item.id); await reload(); } }}>Delete</Button>
    </div> },
  ];

  return <section className="page">
    <header className="page-header"><div><h1 className="page-title">Budgets & Goals</h1><p className="page-subtitle">Track account-scoped budgets and goals using category, classification, tag, and split-aware filters.</p></div></header>
    {message && <div className="notice error">{message}</div>}
    <form className="panel form-grid" onSubmit={submit}>
      <Select label="Type" value={draft.kind} onChange={(event) => setDraft({ ...draft, kind: event.target.value as BudgetGoalKind })}><option value="budget">Budget</option><option value="goal">Goal</option></Select>
      <Input label="Name" value={draft.name} onChange={(event) => setDraft({ ...draft, name: event.target.value })} required />
      <Select label="Account" value={draft.accountId ?? ''} onChange={(event) => setDraft({ ...draft, accountId: event.target.value || null })}><option value="">All accounts</option>{accounts.map((account) => <option key={account.id} value={account.id}>{account.nickname}</option>)}</Select>
      <Input label="Category" value={draft.category ?? ''} onChange={(event) => setDraft({ ...draft, category: event.target.value })} />
      <Select label="Classification" value={draft.classification ?? ''} onChange={(event) => setDraft({ ...draft, classification: event.target.value ? event.target.value as TransactionClassification : null })}><option value="">Any classification</option>{classifications.map((value) => <option key={value} value={value}>{displayEnum(value)}</option>)}</Select>
      <Input label="Starts" type="date" value={draft.startsOn} onChange={(event) => setDraft({ ...draft, startsOn: event.target.value })} required />
      <Input label="Ends" type="date" value={draft.endsOn} onChange={(event) => setDraft({ ...draft, endsOn: event.target.value })} required />
      <Input label="Target amount" type="number" min="0" step="0.01" value={draft.targetAmount} onChange={(event) => setDraft({ ...draft, targetAmount: Number(event.target.value) })} required />
      <Input label="Currency" value={draft.currency} maxLength={3} onChange={(event) => setDraft({ ...draft, currency: event.target.value.toUpperCase() })} required />
      <Checkbox label="Include split rows" checked={draft.includeSplits} onChange={(event) => setDraft({ ...draft, includeSplits: event.target.checked })} />
      <Checkbox label="Active" checked={draft.isActive} onChange={(event) => setDraft({ ...draft, isActive: event.target.checked })} />
      <div className="field-span"><span className="pm-label">Tags</span><TagSelector tags={tags} selected={draft.tagNames ?? []} onChange={(tagNames) => setDraft({ ...draft, tagNames })} /></div>
      <Button type="submit" leftIcon={editingId ? <Pencil size={14} /> : <Plus size={14} />}>{editingId ? 'Update' : 'Add'}</Button>
      {editingId && <Button type="button" variant="secondary" onClick={() => { setEditingId(null); setDraft(emptyDraft); }}>Cancel</Button>}
    </form>
    <div className="panel">
      <div className="panel-header"><h2>Budgets & Goals</h2><Select aria-label="Budget goal filter" value={filter} onChange={(event) => setFilter(event.target.value as 'all' | BudgetGoalKind)}><option value="all">All</option><option value="budget">Budgets</option><option value="goal">Goals</option></Select></div>
      {items.length === 0 ? <EmptyState title="No budgets or goals yet." /> : <Table data={items} columns={columns} rowKey="id" />}
    </div>
  </section>;
}

function TagSelector({ tags, selected, onChange }: { tags: TagDto[]; selected: string[]; onChange: (names: string[]) => void }) {
  if (tags.length === 0) return <span className="muted">Create tags from the Tags page first.</span>;
  return <div className="selector-grid">{tags.map((tag) => <Checkbox key={tag.id} label={tag.name} checked={selected.some((name) => name.toLowerCase() === tag.name.toLowerCase())} onChange={(event) => onChange(event.target.checked ? [...selected, tag.name] : selected.filter((name) => name.toLowerCase() !== tag.name.toLowerCase()))} />)}</div>;
}

function Progress({ value }: { value: number }) {
  const width = `${Math.min(Math.max(value, 0), 100)}%`;
  return <div className="progress-cell"><div className="progress-track"><div className="progress-fill" style={{ width }} /></div><span>{value.toFixed(1)}%</span></div>;
}

function normalizeDraft(draft: UpsertBudgetGoalRequest): UpsertBudgetGoalRequest {
  return {
    ...draft,
    accountId: draft.accountId || null,
    category: draft.category || null,
    classification: draft.classification || null,
    tagNames: draft.tagNames ?? [],
    currency: draft.currency.toUpperCase(),
  };
}

function scopeLabel(item: BudgetGoalDto, accountNameById: Map<string, string>) {
  const parts = [
    item.accountId ? accountNameById.get(item.accountId) ?? 'Account' : 'All accounts',
    item.category,
    item.classification ? displayEnum(item.classification) : null,
    item.tagNames.length ? `Tags: ${item.tagNames.join(', ')}` : null,
    item.includeSplits ? 'Splits' : null,
  ].filter(Boolean);
  return parts.join(' · ');
}

function statusTone(status: BudgetGoalDto['status']) {
  if (status === 'met') return 'success';
  if (status === 'over_limit') return 'danger';
  if (status === 'near_limit') return 'warning';
  if (status === 'in_progress') return 'info';
  return 'neutral';
}
