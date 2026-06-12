import { FormEvent, useEffect, useState } from 'react';
import { FileDown, Link2, NotebookPen, Trash2, Upload, X } from 'lucide-react';
import { Link } from 'react-router-dom';
import { Button, EmptyState, Input, Select, StatusBadge, Table, type TableColumn } from '../components/ui';
import { formatDate, formatMoney } from '../lib/financeFormat';
import { listNotes } from '../services/organizationService';
import { deleteReceipt, dismissReceipt, listReceipts, receiptDownloadUrl, uploadReceipt } from '../services/receiptService';
import type { NoteDto, ReceiptAttachmentDto } from '../types/schema';

export function AttachmentsPage() {
  const [receipts, setReceipts] = useState<ReceiptAttachmentDto[]>([]);
  const [notes, setNotes] = useState<NoteDto[]>([]);
  const [filter, setFilter] = useState('unmatched');
  const [file, setFile] = useState<File | null>(null);
  const [draft, setDraft] = useState({ title: '', notes: '', merchantHint: '', amountHint: '', dateHint: '', transactionId: '' });
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => { void reload(); }, [filter]);

  async function reload() {
    try {
      const status = filter === 'all' ? undefined : filter;
      const [receiptItems, noteItems] = await Promise.all([listReceipts(status), listNotes(status)]);
      setReceipts(receiptItems);
      setNotes(noteItems);
      setMessage(null);
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to load attachments.');
    }
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!file) {
      setMessage('Choose a receipt file.');
      return;
    }

    setSaving(true);
    setMessage(null);
    try {
      await uploadReceipt(file, {
        title: draft.title,
        notes: draft.notes || null,
        merchantHint: draft.merchantHint || null,
        amountHint: draft.amountHint ? Number(draft.amountHint) : null,
        dateHint: draft.dateHint || null,
        transactionId: draft.transactionId || null,
      });
      setFile(null);
      setDraft({ title: '', notes: '', merchantHint: '', amountHint: '', dateHint: '', transactionId: '' });
      const input = document.getElementById('receipt-file') as HTMLInputElement | null;
      if (input) input.value = '';
      await reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to upload receipt.');
    } finally {
      setSaving(false);
    }
  }

  const receiptColumns: TableColumn<ReceiptAttachmentDto>[] = [
    { key: 'title', label: 'Receipt', render: (item) => <div><strong>{item.title}</strong>{item.notes && <div className="muted">{item.notes}</div>}</div> },
    { key: 'hints', label: 'Matching hints', render: (item) => [item.merchantHint, item.amountHint ? formatMoney(item.amountHint) : null, item.dateHint].filter(Boolean).join(' · ') || 'None' },
    { key: 'file', label: 'File', render: (item) => <a className="table-resource-link" href={receiptDownloadUrl(item.id)}><FileDown size={14} />{item.originalFileName}</a> },
    { key: 'sizeBytes', label: 'Size', align: 'right', render: (item) => `${Math.max(1, Math.round(item.sizeBytes / 1024))} KB` },
    { key: 'status', label: 'Status', render: (item) => <StatusBadge label={item.status} tone={item.status === 'matched' ? 'success' : item.status === 'dismissed' ? 'neutral' : 'warning'} /> },
    { key: 'transactionId', label: 'Transaction', render: (item) => item.transactionId ?? 'Unmatched' },
    { key: 'actions', label: '', align: 'right', render: (item) => <div className="row-actions">
      {item.status === 'unmatched' && <Button type="button" size="sm" variant="secondary" leftIcon={<X size={14} />} onClick={async () => { await dismissReceipt(item.id); await reload(); }}>Dismiss</Button>}
      <Button type="button" size="sm" variant="destructive" leftIcon={<Trash2 size={14} />} onClick={async () => { if (window.confirm(`Delete receipt ${item.title}?`)) { await deleteReceipt(item.id); await reload(); } }}>Delete</Button>
    </div> },
  ];

  const noteColumns: TableColumn<NoteDto>[] = [
    { key: 'title', label: 'Note', render: (note) => <div><strong>{note.title}</strong>{note.body && <div className="muted">{note.body}</div>}</div> },
    { key: 'hints', label: 'Matching hints', render: (note) => [note.merchantHint, note.amountHint ? formatMoney(note.amountHint) : null, note.dateHint].filter(Boolean).join(' · ') || 'None' },
    { key: 'status', label: 'Status', render: (note) => <StatusBadge label={note.status} tone={note.status === 'matched' ? 'success' : note.status === 'dismissed' ? 'neutral' : 'warning'} /> },
    { key: 'matchedTransactionId', label: 'Transaction', render: (note) => note.matchedTransactionId ?? 'Unmatched' },
  ];

  return <section className="page">
    <header className="page-header"><div><h1 className="page-title">Attachments</h1><p className="page-subtitle">Pre-upload receipts or keep note-style transaction context, then match them to ledger rows when the transaction arrives.</p></div></header>
    {message && <div className="notice error">{message}</div>}
    <form className="panel form-grid" onSubmit={submit}>
      <Input id="receipt-file" label="Receipt file" type="file" onChange={(event) => { const next = event.target.files?.[0] ?? null; setFile(next); if (next && !draft.title) setDraft({ ...draft, title: next.name }); }} required />
      <Input label="Title" value={draft.title} onChange={(event) => setDraft({ ...draft, title: event.target.value })} required />
      <Input label="Notes" value={draft.notes} onChange={(event) => setDraft({ ...draft, notes: event.target.value })} />
      <Input label="Merchant hint" value={draft.merchantHint} onChange={(event) => setDraft({ ...draft, merchantHint: event.target.value })} />
      <Input label="Amount hint" type="number" min="0" step="0.01" value={draft.amountHint} onChange={(event) => setDraft({ ...draft, amountHint: event.target.value })} />
      <Input label="Date hint" type="date" value={draft.dateHint} onChange={(event) => setDraft({ ...draft, dateHint: event.target.value })} />
      <Input label="Transaction ID" value={draft.transactionId} onChange={(event) => setDraft({ ...draft, transactionId: event.target.value })} />
      <Button type="submit" loading={saving} leftIcon={<Upload size={15} />}>Upload Receipt</Button>
    </form>
    <div className="panel">
      <div className="panel-header"><h2>Receipts</h2><Select aria-label="Receipt status filter" value={filter} onChange={(event) => setFilter(event.target.value)}><option value="unmatched">Unmatched</option><option value="matched">Matched</option><option value="dismissed">Dismissed</option><option value="all">All</option></Select></div>
      {receipts.length === 0 ? <EmptyState title="No receipts in this view." /> : <Table data={receipts} columns={receiptColumns} rowKey="id" />}
    </div>
    <div className="panel">
      <div className="panel-header"><h2>Notes</h2><Link className="table-resource-link" to="/notes"><NotebookPen size={14} />Manage Notes</Link></div>
      {notes.length === 0 ? <EmptyState title="No notes in this view." /> : <Table data={notes} columns={noteColumns} rowKey="id" />}
      <div className="row-actions organize-actions"><Link className="table-resource-link" to="/transactions"><Link2 size={14} />Match from Transactions</Link></div>
    </div>
  </section>;
}
