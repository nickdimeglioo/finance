import { FormEvent, useEffect, useState } from 'react';
import { Pencil, Plus, Trash2, X } from 'lucide-react';
import { Button, EmptyState, Input, Table, type TableColumn } from '../components/ui';
import { createTag, deleteTag, listTags, updateTag } from '../services/organizationService';
import type { TagDto } from '../types/schema';

export function TagsPage() {
  const [tags, setTags] = useState<TagDto[]>([]);
  const [name, setName] = useState('');
  const [color, setColor] = useState('#1d5d73');
  const [editingId, setEditingId] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  async function reload() {
    try { setTags(await listTags()); setMessage(null); } catch (err) { setMessage(err instanceof Error ? err.message : 'Unable to load tags.'); }
  }
  useEffect(() => { void reload(); }, []);

  async function submit(event: FormEvent) {
    event.preventDefault();
    try {
      if (editingId) await updateTag(editingId, { name, color });
      else await createTag({ name, color });
      setName(''); setColor('#1d5d73'); setEditingId(null); await reload();
    } catch (err) { setMessage(err instanceof Error ? err.message : 'Unable to save tag.'); }
  }

  const columns: TableColumn<TagDto>[] = [
    { key: 'color', label: 'Color', render: (tag) => <span className="color-swatch" style={{ background: tag.color ?? '#c3ced6' }} /> },
    { key: 'name', label: 'Name' },
    { key: 'transactionCount', label: 'Transactions', align: 'right' },
    { key: 'actions', label: '', align: 'right', render: (tag) => <div className="row-actions">
      <Button type="button" variant="secondary" size="sm" leftIcon={<Pencil size={14} />} onClick={() => { setEditingId(tag.id); setName(tag.name); setColor(tag.color ?? '#1d5d73'); }}>Edit</Button>
      <Button type="button" variant="destructive" size="sm" leftIcon={<Trash2 size={14} />} onClick={async () => { if (window.confirm(`Delete tag ${tag.name}?`)) { await deleteTag(tag.id); await reload(); } }}>Delete</Button>
    </div> },
  ];

  return <section className="page">
    <header className="page-header"><div><h1 className="page-title">Tags</h1><p className="page-subtitle">Create, rename, recolor, and remove transaction tags.</p></div></header>
    {message && <div className="notice error">{message}</div>}
    <form className="panel inline-actions tag-editor" onSubmit={submit}>
      <Input label={editingId ? 'Rename tag' : 'New tag'} value={name} onChange={(event) => setName(event.target.value)} required />
      <Input label="Color" type="color" value={color} onChange={(event) => setColor(event.target.value)} />
      <Button type="submit" leftIcon={editingId ? <Pencil size={14} /> : <Plus size={14} />}>{editingId ? 'Update' : 'Create'}</Button>
      {editingId && <Button type="button" variant="secondary" leftIcon={<X size={14} />} onClick={() => { setEditingId(null); setName(''); }}>Cancel</Button>}
    </form>
    <div className="panel">{tags.length === 0 ? <EmptyState title="No tags yet." /> : <Table data={tags} columns={columns} rowKey="id" />}</div>
  </section>;
}
