import { FormEvent, useEffect, useMemo, useState } from 'react';
import { Ban, ChevronLeft, ChevronRight, CreditCard, FilterX, Link2, Save, Tags } from 'lucide-react';
import { Button, Checkbox, EmptyState, Input, Select, StatusBadge, Table, type TableColumn } from '../components/ui';
import { displayEnum } from '../lib/display';
import { formatDate, formatMoney, toApiDate } from '../lib/financeFormat';
import { useAccounts } from '../services/accountsService';
import { acceptNoteMatch, findNoteMatches, listTags } from '../services/organizationService';
import { createTransaction, createTransfer, getCreditCardPaymentDrilldown, replaceTransactionTags, updateTransactionStatus, useTransactions, voidTransaction } from '../services/transactionsService';
import type { CreateTransactionRequest, CreateTransferRequest, CreditCardPaymentDrilldownDto, NoteMatchSuggestionDto, TagDto, TransactionClassification, TransactionFiltersRequest, TransactionListItemDto, TransactionStatus, TransactionType } from '../types/schema';

const types: TransactionType[] = ['expense', 'income', 'adjustment'];
const classifications: TransactionClassification[] = ['personal', 'business', 'transfer', 'investment', 'tax', 'reimbursement', 'exclude', 'mixed', 'ignored', 'unknown'];
const statuses: TransactionStatus[] = ['pending', 'posted', 'reconciled', 'voided'];

export function TransactionsPage() {
  const { accounts } = useAccounts();
  const [filters, setFilters] = useState({
    accountId: '',
    from: '',
    to: '',
    search: '',
    type: '',
    classification: '',
    category: '',
    tagId: '',
    status: '',
    amountMin: '',
    amountMax: '',
    includeVoided: false,
  });
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [tags, setTags] = useState<TagDto[]>([]);
  const [newTagIds, setNewTagIds] = useState<string[]>([]);
  const [selectedTransaction, setSelectedTransaction] = useState<TransactionListItemDto | null>(null);
  const [drilldownTransaction, setDrilldownTransaction] = useState<TransactionListItemDto | null>(null);
  const [drilldown, setDrilldown] = useState<CreditCardPaymentDrilldownDto | null>(null);
  const [selectedTagIds, setSelectedTagIds] = useState<string[]>([]);
  const [noteMatches, setNoteMatches] = useState<NoteMatchSuggestionDto[]>([]);
  const [mode, setMode] = useState<'transaction' | 'transfer'>('transaction');
  const [message, setMessage] = useState<string | null>(null);
  const [transaction, setTransaction] = useState<CreateTransactionRequest>({
    accountId: '', date: toApiDate(new Date()), description: '', type: 'expense', classification: 'personal', amount: 0, currency: 'USD',
  });
  const [transfer, setTransfer] = useState<CreateTransferRequest>({
    fromAccountId: '', toAccountId: '', date: toApiDate(new Date()), description: '', amount: 0, currency: 'USD', classification: 'personal',
  });
  const tagColorByName = useMemo(() => new Map(tags.map((tag) => [tag.name.toLowerCase(), tag.color])), [tags]);
  const accountNameById = useMemo(() => new Map(accounts.map((account) => [account.id, account.nickname])), [accounts]);
  const transactionFilters = useMemo<TransactionFiltersRequest>(() => ({
    accountId: filters.accountId || undefined,
    from: filters.from || undefined,
    to: filters.to || undefined,
    search: filters.search || undefined,
    type: filters.type ? filters.type as TransactionType : undefined,
    classification: filters.classification ? filters.classification as TransactionClassification : undefined,
    category: filters.category || undefined,
    tagId: filters.tagId || undefined,
    status: filters.status ? filters.status as TransactionStatus : undefined,
    amountMin: filters.amountMin === '' ? undefined : Number(filters.amountMin),
    amountMax: filters.amountMax === '' ? undefined : Number(filters.amountMax),
    includeVoided: filters.includeVoided,
    page,
    pageSize,
  }), [filters, page, pageSize]);
  const { result, loading, error, reload } = useTransactions(transactionFilters);
  const totalPages = Math.max(1, Math.ceil(result.totalCount / result.pageSize));

  useEffect(() => { listTags().then(setTags).catch((err: Error) => setMessage(err.message)); }, []);

  function updateFilter<K extends keyof typeof filters>(key: K, value: (typeof filters)[K]) {
    setFilters((current) => ({ ...current, [key]: value }));
    setPage(1);
  }

  function clearFilters() {
    setFilters({
      accountId: '',
      from: '',
      to: '',
      search: '',
      type: '',
      classification: '',
      category: '',
      tagId: '',
      status: '',
      amountMin: '',
      amountMax: '',
      includeVoided: false,
    });
    setPage(1);
  }

  async function submit(event: FormEvent) {
    event.preventDefault(); setMessage(null);
    try {
      if (mode === 'transfer') {
        await createTransfer(transfer);
      } else {
        const created = await createTransaction(transaction);
        if (newTagIds.length > 0) await replaceTransactionTags(created.id, newTagIds);
      }
      await reload();
    } catch (err) { setMessage(err instanceof Error ? err.message : 'Unable to save transaction.'); }
  }

  function openTags(item: TransactionListItemDto) {
    setSelectedTransaction(item);
    setSelectedTagIds(tags.filter((tag) => hasTagName(item.tags, tag.name)).map((tag) => tag.id));
    setNoteMatches([]);
  }

  async function findMatches(item: TransactionListItemDto) {
    setSelectedTransaction(item);
    setSelectedTagIds(tags.filter((tag) => hasTagName(item.tags, tag.name)).map((tag) => tag.id));
    try { setNoteMatches(await findNoteMatches(item.id)); } catch (err) { setMessage(err instanceof Error ? err.message : 'Unable to find note matches.'); }
  }

  async function openDrilldown(item: TransactionListItemDto) {
    setDrilldownTransaction(item);
    try {
      setDrilldown(await getCreditCardPaymentDrilldown(item.id));
    } catch (err) {
      setDrilldown(null);
      setMessage(err instanceof Error ? err.message : 'Unable to load payment drilldown.');
    }
  }

  const columns: TableColumn<TransactionListItemDto>[] = [
    { key: 'date', label: 'Date', render: (item) => formatDate(item.date) },
    { key: 'accountId', label: 'Account', width: 125, render: (item) => accountNameById.get(item.accountId) ?? 'Account' },
    { key: 'description', label: 'Description', width: 200 },
    { key: 'type', label: 'Type', render: (item) => displayEnum(item.type) },
    { key: 'classification', label: 'Class', render: (item) => displayEnum(item.classification) },
    { key: 'category', label: 'Category', width: 120, render: (item) => item.category ?? '' },
    { key: 'tags', label: 'Tags', width: 170, render: (item) => <div className="tag-list">{item.tags.map((tag) => <span className="tag-chip" style={tagChipStyle(tagColorByName.get(tag.toLowerCase()))} key={tag}>{tag}</span>)}</div> },
    { key: 'status', label: 'Status', width: 120, render: (item) => <Select aria-label="Transaction status" value={item.status} onChange={async (event) => { await updateTransactionStatus(item.id, event.target.value as TransactionStatus); await reload(); }}>{statuses.map((status) => <option key={status} value={status}>{displayEnum(status)}</option>)}</Select> },
    { key: 'amount', label: 'Amount', width: 105, align: 'right', render: (item) => formatMoney(item.amount, item.currency) },
    { key: 'actions', label: '', width: 230, align: 'right', render: (item) => <div className="row-actions">
      <Button type="button" variant="secondary" size="sm" leftIcon={<Tags size={14} />} onClick={() => openTags(item)}>Tags</Button>
      <Button type="button" variant="secondary" size="sm" leftIcon={<Link2 size={14} />} onClick={() => findMatches(item)}>Notes</Button>
      {canReviewPayment(item, accounts.find((account) => account.id === item.accountId)?.type) && <Button type="button" variant="secondary" size="sm" leftIcon={<CreditCard size={14} />} onClick={() => openDrilldown(item)}>Review</Button>}
      {!item.isVoid ? <Button type="button" variant="secondary" size="sm" leftIcon={<Ban size={15} />} onClick={async () => { await voidTransaction(item.id, item.type === 'transfer'); await reload(); }}>Void</Button> : <StatusBadge label="Voided" tone="danger" />}
    </div> },
  ];

  return <section className="page">
    <header className="page-header"><div><h1 className="page-title">Transactions</h1><p className="page-subtitle">Manual entry, tag assignment, note matching, transfers, voiding, and status changes.</p></div></header>
    {message && <div className="notice error">{message}</div>}
    <form className="panel form-grid" onSubmit={submit}>
      <Select label="Mode" value={mode} onChange={(event) => setMode(event.target.value as 'transaction' | 'transfer')}><option value="transaction">Transaction</option><option value="transfer">Transfer</option></Select>
      {mode === 'transaction' ? <>
        <Select label="Account" value={transaction.accountId} onChange={(event) => setTransaction({ ...transaction, accountId: event.target.value })} required><option value="">Select account</option>{accounts.map((account) => <option key={account.id} value={account.id}>{account.nickname}</option>)}</Select>
        <Select label="Type" value={transaction.type} onChange={(event) => setTransaction({ ...transaction, type: event.target.value as TransactionType })}>{types.map((type) => <option key={type} value={type}>{displayEnum(type)}</option>)}</Select>
      </> : <>
        <Select label="From" value={transfer.fromAccountId} onChange={(event) => setTransfer({ ...transfer, fromAccountId: event.target.value })} required><option value="">Select account</option>{accounts.map((account) => <option key={account.id} value={account.id}>{account.nickname}</option>)}</Select>
        <Select label="To" value={transfer.toAccountId} onChange={(event) => setTransfer({ ...transfer, toAccountId: event.target.value })} required><option value="">Select account</option>{accounts.map((account) => <option key={account.id} value={account.id}>{account.nickname}</option>)}</Select>
      </>}
      <Input label="Date" type="date" value={mode === 'transfer' ? transfer.date : transaction.date} onChange={(event) => mode === 'transfer' ? setTransfer({ ...transfer, date: event.target.value }) : setTransaction({ ...transaction, date: event.target.value })} />
      <Input label="Description" value={mode === 'transfer' ? transfer.description : transaction.description} onChange={(event) => mode === 'transfer' ? setTransfer({ ...transfer, description: event.target.value }) : setTransaction({ ...transaction, description: event.target.value })} required />
      <Input label="Amount" type="number" step="0.01" value={mode === 'transfer' ? transfer.amount : transaction.amount} onChange={(event) => mode === 'transfer' ? setTransfer({ ...transfer, amount: Number(event.target.value) }) : setTransaction({ ...transaction, amount: Number(event.target.value) })} />
      <Select label="Classification" value={mode === 'transfer' ? transfer.classification : transaction.classification} onChange={(event) => mode === 'transfer' ? setTransfer({ ...transfer, classification: event.target.value as TransactionClassification }) : setTransaction({ ...transaction, classification: event.target.value as TransactionClassification })}>{classifications.map((value) => <option key={value} value={value}>{displayEnum(value)}</option>)}</Select>
      <Button type="submit" leftIcon={<Save size={15} />}>Save</Button>
      {mode === 'transaction' && tags.length > 0 && <div className="field-span"><span className="pm-label">Tags for new transaction</span><TagSelector tags={tags} selected={newTagIds} onChange={setNewTagIds} /></div>}
    </form>

    {selectedTransaction && <div className="panel">
      <div className="panel-header"><h2>Organize: {selectedTransaction.description}</h2><Button type="button" variant="secondary" size="sm" onClick={() => { setSelectedTransaction(null); setNoteMatches([]); }}>Close</Button></div>
      <TagSelector tags={tags} selected={selectedTagIds} onChange={setSelectedTagIds} />
      <div className="row-actions organize-actions">
        <Button type="button" leftIcon={<Save size={14} />} onClick={async () => { await replaceTransactionTags(selectedTransaction.id, selectedTagIds); await reload(); setSelectedTransaction(null); }}>Save Tags</Button>
        <Button type="button" variant="secondary" leftIcon={<Link2 size={14} />} onClick={() => findMatches(selectedTransaction)}>Find Note Matches</Button>
      </div>
      {noteMatches.length > 0 && <div className="match-list">{noteMatches.map((match) => <div className="match-card" key={match.note.id}><div><strong>{match.note.title}</strong><div className="muted">Score {match.score}: {match.reasons.join(', ')}</div></div><Button type="button" size="sm" onClick={async () => { await acceptNoteMatch(match.note.id, selectedTransaction.id); setNoteMatches(noteMatches.filter((item) => item.note.id !== match.note.id)); }}>Match</Button></div>)}</div>}
      {noteMatches.length === 0 && <p className="muted">Use Find Note Matches to score unmatched notes for this transaction.</p>}
    </div>}

    {drilldownTransaction && <div className="panel">
      <div className="panel-header">
        <h2>Payment Drilldown: {drilldownTransaction.description}</h2>
        <Button type="button" variant="secondary" size="sm" onClick={() => { setDrilldownTransaction(null); setDrilldown(null); }}>Close</Button>
      </div>
      {drilldown ? <>
        <div className="drilldown-summary">
          <span><strong>{drilldown.accountName}</strong></span>
          <span>Before {formatMoney(drilldown.balanceBeforePayment, drilldownTransaction.currency)}</span>
          <span>After {formatMoney(drilldown.balanceAfterPayment, drilldownTransaction.currency)}</span>
          <span>Payment {formatMoney(drilldown.paymentAmount, drilldownTransaction.currency)}</span>
          <span>Applied {formatMoney(drilldown.appliedAmount, drilldownTransaction.currency)} ({drilldown.paymentAppliedPercent.toFixed(1)}%)</span>
          <span>Balance paid {drilldown.balancePaidPercent.toFixed(1)}%</span>
          <span>Unapplied {formatMoney(drilldown.unappliedAmount, drilldownTransaction.currency)}</span>
          <span>Unpaid now {formatMoney(drilldown.currentUnpaidAmount, drilldownTransaction.currency)}</span>
        </div>
        <h3 className="panel-subtitle">Covered by this payment</h3>
        {drilldown.coveredRows.length
          ? <Table
            data={drilldown.coveredRows}
            rowKey="transactionId"
            columns={[
              { key: 'date', label: 'Date', render: (row) => formatDate(row.date) },
              { key: 'description', label: 'Description', width: 260 },
              { key: 'category', label: 'Category', render: (row) => row.category ?? '' },
              { key: 'originalAmount', label: 'Original', align: 'right', render: (row) => formatMoney(row.originalAmount, row.currency) },
              { key: 'outstandingBeforePayment', label: 'Unpaid Before', align: 'right', render: (row) => formatMoney(row.outstandingBeforePayment, row.currency) },
              { key: 'coveredAmount', label: 'Covered', align: 'right', render: (row) => formatMoney(row.coveredAmount, row.currency) },
              { key: 'remainingAmount', label: 'Remaining', align: 'right', render: (row) => formatMoney(row.remainingAmount, row.currency) },
              { key: 'coveredPercent', label: 'Paid', align: 'right', render: (row) => `${row.coveredPercent.toFixed(1)}%` },
            ]}
          />
          : <p className="muted">No prior credit-card charges were covered by this payment.</p>}
        <h3 className="panel-subtitle">Currently Unpaid Charges</h3>
        {drilldown.unpaidRows.length
          ? <Table
            data={drilldown.unpaidRows}
            rowKey="transactionId"
            columns={[
              { key: 'date', label: 'Date', render: (row) => formatDate(row.date) },
              { key: 'description', label: 'Description', width: 260 },
              { key: 'category', label: 'Category', render: (row) => row.category ?? '' },
              { key: 'originalAmount', label: 'Original', align: 'right', render: (row) => formatMoney(row.originalAmount, row.currency) },
              { key: 'paidAmount', label: 'Paid', align: 'right', render: (row) => formatMoney(row.paidAmount, row.currency) },
              { key: 'remainingAmount', label: 'Unpaid', align: 'right', render: (row) => formatMoney(row.remainingAmount, row.currency) },
              { key: 'paidPercent', label: 'Paid %', align: 'right', render: (row) => `${row.paidPercent.toFixed(1)}%` },
            ]}
          />
          : <p className="muted">No unpaid posted credit-card charges remain in the ledger.</p>}
      </> : <p className="muted">Loading payment drilldown...</p>}
    </div>}

    <div className="panel">
      <div className="panel-header"><h2>Transactions</h2><Button type="button" variant="secondary" size="sm" leftIcon={<FilterX size={14} />} onClick={clearFilters}>Clear Filters</Button></div>
      <div className="transaction-filter-grid">
        <Select label="Account" value={filters.accountId} onChange={(event) => updateFilter('accountId', event.target.value)}><option value="">All accounts</option>{accounts.map((account) => <option key={account.id} value={account.id}>{account.nickname}</option>)}</Select>
        <Input label="From" type="date" value={filters.from} onChange={(event) => updateFilter('from', event.target.value)} />
        <Input label="To" type="date" value={filters.to} onChange={(event) => updateFilter('to', event.target.value)} />
        <Input label="Description" value={filters.search} placeholder="Search text" onChange={(event) => updateFilter('search', event.target.value)} />
        <Select label="Type" value={filters.type} onChange={(event) => updateFilter('type', event.target.value)}><option value="">All types</option>{types.map((type) => <option key={type} value={type}>{displayEnum(type)}</option>)}</Select>
        <Select label="Class" value={filters.classification} onChange={(event) => updateFilter('classification', event.target.value)}><option value="">All classes</option>{classifications.map((value) => <option key={value} value={value}>{displayEnum(value)}</option>)}</Select>
        <Input label="Category" value={filters.category} onChange={(event) => updateFilter('category', event.target.value)} />
        <Select label="Tags" value={filters.tagId} onChange={(event) => updateFilter('tagId', event.target.value)}><option value="">All tags</option>{tags.map((tag) => <option key={tag.id} value={tag.id}>{tag.name} ({tag.transactionCount})</option>)}</Select>
        <Select label="Status" value={filters.status} onChange={(event) => updateFilter('status', event.target.value)}><option value="">All statuses</option>{statuses.map((status) => <option key={status} value={status}>{displayEnum(status)}</option>)}</Select>
        <Input label="Min amount" type="number" min="0" step="0.01" value={filters.amountMin} onChange={(event) => updateFilter('amountMin', event.target.value)} />
        <Input label="Max amount" type="number" min="0" step="0.01" value={filters.amountMax} onChange={(event) => updateFilter('amountMax', event.target.value)} />
        <Checkbox label="Include voided" checked={filters.includeVoided} onChange={(event) => updateFilter('includeVoided', event.target.checked)} />
      </div>
      {error && <p>{error.message}</p>}
      {!error && result.items.length === 0 && !loading ? <EmptyState title="No transactions found." /> : null}
      {!error && (result.items.length > 0 || loading) ? <Table data={result.items} columns={columns} rowKey="id" loading={loading} emptyMessage="No transactions found." /> : null}
      <div className="pagination-bar">
        <span>{result.totalCount === 0 ? '0 transactions' : `Page ${result.page} of ${totalPages} (${result.totalCount} transactions)`}</span>
        <div className="pagination-controls">
          <Select aria-label="Rows per page" value={pageSize} onChange={(event) => { setPageSize(Number(event.target.value)); setPage(1); }}>
            {[10, 25, 50].map((size) => <option key={size} value={size}>{size} per page</option>)}
          </Select>
          <Button type="button" variant="secondary" size="sm" leftIcon={<ChevronLeft size={14} />} onClick={() => setPage((current) => Math.max(1, current - 1))} disabled={page <= 1}>Prev</Button>
          <Button type="button" variant="secondary" size="sm" rightIcon={<ChevronRight size={14} />} onClick={() => setPage((current) => Math.min(totalPages, current + 1))} disabled={page >= totalPages}>Next</Button>
        </div>
      </div>
    </div>
  </section>;
}

function TagSelector({ tags, selected, onChange }: { tags: TagDto[]; selected: string[]; onChange: (ids: string[]) => void }) {
  if (tags.length === 0) return <span className="muted">Create tags from the Tags page first.</span>;
  return <div className="selector-grid">{tags.map((tag) => <Checkbox key={tag.id} label={tag.name} checked={selected.includes(tag.id)} onChange={(event) => onChange(event.target.checked ? [...selected, tag.id] : selected.filter((id) => id !== tag.id))} />)}</div>;
}

function hasTagName(names: string[], tagName: string) {
  return names.some((name) => name.localeCompare(tagName, undefined, { sensitivity: 'accent' }) === 0);
}

function tagChipStyle(color?: string | null) {
  return color ? { borderColor: color, background: `${color}24` } : undefined;
}

function canReviewPayment(item: TransactionListItemDto, accountType?: string) {
  if (item.hasTransferLinkSuggestion || item.transferPartnerId) return true;
  return accountType === 'credit_card' && (item.direction === 'inflow' || item.type === 'income' || item.type === 'transfer' || item.type === 'opening_balance');
}
