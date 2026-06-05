import { FormEvent, useEffect, useState } from 'react';
import { Ban, Link2, Save, Tags } from 'lucide-react';
import { Button, Checkbox, EmptyState, Input, Select, StatusBadge, Table, type TableColumn } from '../components/ui';
import { displayEnum } from '../lib/display';
import { formatDate, formatMoney, toApiDate } from '../lib/financeFormat';
import { useAccounts } from '../services/accountsService';
import { acceptNoteMatch, findNoteMatches, listTags } from '../services/organizationService';
import { createTransaction, createTransfer, replaceTransactionTags, updateTransactionStatus, useTransactions, voidTransaction } from '../services/transactionsService';
import type { CreateTransactionRequest, CreateTransferRequest, NoteMatchSuggestionDto, TagDto, TransactionClassification, TransactionListItemDto, TransactionStatus, TransactionType } from '../types/schema';

const types: TransactionType[] = ['expense', 'income', 'adjustment'];
const classifications: TransactionClassification[] = ['personal', 'business', 'mixed', 'ignored', 'unknown'];
const statuses: TransactionStatus[] = ['pending', 'posted', 'reconciled', 'voided'];

export function TransactionsPage() {
  const { accounts } = useAccounts();
  const [tagFilter, setTagFilter] = useState('');
  const { result, loading, error, reload } = useTransactions({ tagId: tagFilter || undefined, page: 1, pageSize: 100 });
  const [tags, setTags] = useState<TagDto[]>([]);
  const [newTagIds, setNewTagIds] = useState<string[]>([]);
  const [selectedTransaction, setSelectedTransaction] = useState<TransactionListItemDto | null>(null);
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

  useEffect(() => { listTags().then(setTags).catch((err: Error) => setMessage(err.message)); }, []);

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
    setSelectedTagIds(tags.filter((tag) => item.tags.includes(tag.name)).map((tag) => tag.id));
    setNoteMatches([]);
  }

  async function findMatches(item: TransactionListItemDto) {
    setSelectedTransaction(item);
    setSelectedTagIds(tags.filter((tag) => item.tags.includes(tag.name)).map((tag) => tag.id));
    try { setNoteMatches(await findNoteMatches(item.id)); } catch (err) { setMessage(err instanceof Error ? err.message : 'Unable to find note matches.'); }
  }

  const columns: TableColumn<TransactionListItemDto>[] = [
    { key: 'date', label: 'Date', render: (item) => formatDate(item.date) },
    { key: 'description', label: 'Description' },
    { key: 'type', label: 'Type', render: (item) => displayEnum(item.type) },
    { key: 'classification', label: 'Class', render: (item) => displayEnum(item.classification) },
    { key: 'tags', label: 'Tags', render: (item) => <div className="tag-list">{item.tags.map((tag) => <span className="tag-chip" key={tag}>{tag}</span>)}</div> },
    { key: 'status', label: 'Status', render: (item) => <Select aria-label="Transaction status" value={item.status} onChange={async (event) => { await updateTransactionStatus(item.id, event.target.value as TransactionStatus); await reload(); }}>{statuses.map((status) => <option key={status} value={status}>{displayEnum(status)}</option>)}</Select> },
    { key: 'amount', label: 'Amount', align: 'right', render: (item) => formatMoney(item.amount, item.currency) },
    { key: 'actions', label: '', align: 'right', render: (item) => <div className="row-actions">
      <Button type="button" variant="secondary" size="sm" leftIcon={<Tags size={14} />} onClick={() => openTags(item)}>Tags</Button>
      <Button type="button" variant="secondary" size="sm" leftIcon={<Link2 size={14} />} onClick={() => findMatches(item)}>Notes</Button>
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

    <div className="panel">
      <div className="panel-header"><h2>Transactions</h2><Select aria-label="Tag filter" value={tagFilter} onChange={(event) => setTagFilter(event.target.value)}><option value="">All tags</option>{tags.map((tag) => <option key={tag.id} value={tag.id}>{tag.name} ({tag.transactionCount})</option>)}</Select></div>
      {error && <p>{error.message}</p>}
      {!error && result.items.length === 0 && !loading ? <EmptyState title="No transactions found." /> : null}
      {!error && (result.items.length > 0 || loading) ? <Table data={result.items} columns={columns} rowKey="id" loading={loading} emptyMessage="No transactions found." /> : null}
    </div>
  </section>;
}

function TagSelector({ tags, selected, onChange }: { tags: TagDto[]; selected: string[]; onChange: (ids: string[]) => void }) {
  if (tags.length === 0) return <span className="muted">Create tags from the Tags page first.</span>;
  return <div className="selector-grid">{tags.map((tag) => <Checkbox key={tag.id} label={tag.name} checked={selected.includes(tag.id)} onChange={(event) => onChange(event.target.checked ? [...selected, tag.id] : selected.filter((id) => id !== tag.id))} />)}</div>;
}
