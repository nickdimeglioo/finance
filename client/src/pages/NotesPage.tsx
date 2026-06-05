import { FormEvent, useEffect, useState } from 'react';
import { Pencil, Plus, Trash2, X } from 'lucide-react';
import { Button, EmptyState, Input, Select, StatusBadge, Table, type TableColumn } from '../components/ui';
import { createNote, deleteNote, dismissNote, listNotes, updateNote } from '../services/organizationService';
import type { NoteDto, UpsertNoteRequest } from '../types/schema';

const emptyNote: UpsertNoteRequest = { title: '', body: '', merchantHint: '', amountHint: null, dateHint: null, remindOn: null };

export function NotesPage() {
  const [notes, setNotes] = useState<NoteDto[]>([]);
  const [filter, setFilter] = useState('unmatched');
  const [draft, setDraft] = useState<UpsertNoteRequest>(emptyNote);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  async function reload(nextFilter = filter) {
    try { setNotes(await listNotes(nextFilter === 'all' ? undefined : nextFilter)); setMessage(null); } catch (err) { setMessage(err instanceof Error ? err.message : 'Unable to load notes.'); }
  }
  useEffect(() => { void reload(); }, [filter]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    try {
      if (editingId) await updateNote(editingId, draft); else await createNote(draft);
      setDraft(emptyNote); setEditingId(null); await reload();
    } catch (err) { setMessage(err instanceof Error ? err.message : 'Unable to save note.'); }
  }

  const columns: TableColumn<NoteDto>[] = [
    { key: 'title', label: 'Note', render: (note) => <div><strong>{note.title}</strong>{note.body && <div className="muted">{note.body}</div>}</div> },
    { key: 'hints', label: 'Matching hints', render: (note) => [note.merchantHint, note.amountHint, note.dateHint].filter(Boolean).join(' · ') || 'None' },
    { key: 'remindOn', label: 'Remind', render: (note) => note.remindOn ?? 'None' },
    { key: 'status', label: 'Status', render: (note) => <StatusBadge label={note.status} tone={note.status === 'matched' ? 'success' : note.status === 'dismissed' ? 'neutral' : 'warning'} /> },
    { key: 'matchedTransactionId', label: 'Matched transaction', render: (note) => note.matchedTransactionId ?? 'Unmatched' },
    { key: 'actions', label: '', align: 'right', render: (note) => <div className="row-actions">
      <Button type="button" size="sm" variant="secondary" leftIcon={<Pencil size={14} />} onClick={() => { setEditingId(note.id); setDraft({ title: note.title, body: note.body, amountHint: note.amountHint, merchantHint: note.merchantHint, dateHint: note.dateHint, remindOn: note.remindOn }); }}>Edit</Button>
      {note.status === 'unmatched' && <Button type="button" size="sm" variant="secondary" leftIcon={<X size={14} />} onClick={async () => { await dismissNote(note.id); await reload(); }}>Dismiss</Button>}
      <Button type="button" size="sm" variant="destructive" leftIcon={<Trash2 size={14} />} onClick={async () => { if (window.confirm(`Delete note ${note.title}?`)) { await deleteNote(note.id); await reload(); } }}>Delete</Button>
    </div> },
  ];

  return <section className="page">
    <header className="page-header"><div><h1 className="page-title">Notes</h1><p className="page-subtitle">Track memory prompts and match them to transactions using explicit hints.</p></div></header>
    {message && <div className="notice error">{message}</div>}
    <form className="panel form-grid" onSubmit={submit}>
      <Input label="Title" value={draft.title} onChange={(event) => setDraft({ ...draft, title: event.target.value })} required />
      <Input label="Body" value={draft.body ?? ''} onChange={(event) => setDraft({ ...draft, body: event.target.value })} />
      <Input label="Merchant hint" value={draft.merchantHint ?? ''} onChange={(event) => setDraft({ ...draft, merchantHint: event.target.value })} />
      <Input label="Amount hint" type="number" min="0" step="0.01" value={draft.amountHint ?? ''} onChange={(event) => setDraft({ ...draft, amountHint: event.target.value ? Number(event.target.value) : null })} />
      <Input label="Date hint" type="date" value={draft.dateHint ?? ''} onChange={(event) => setDraft({ ...draft, dateHint: event.target.value || null })} />
      <Input label="Remind on" type="date" value={draft.remindOn ?? ''} onChange={(event) => setDraft({ ...draft, remindOn: event.target.value || null })} />
      <Button type="submit" leftIcon={editingId ? <Pencil size={14} /> : <Plus size={14} />}>{editingId ? 'Update Note' : 'Add Note'}</Button>
      {editingId && <Button type="button" variant="secondary" onClick={() => { setEditingId(null); setDraft(emptyNote); }}>Cancel</Button>}
    </form>
    <div className="panel">
      <div className="panel-header"><h2>Notes</h2><Select aria-label="Note status filter" value={filter} onChange={(event) => setFilter(event.target.value)}><option value="unmatched">Unmatched</option><option value="matched">Matched</option><option value="dismissed">Dismissed</option><option value="all">All</option></Select></div>
      {notes.length === 0 ? <EmptyState title="No notes in this view." /> : <Table data={notes} columns={columns} rowKey="id" />}
    </div>
  </section>;
}
