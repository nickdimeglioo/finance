import { FormEvent, useEffect, useState } from 'react';
import { Upload } from 'lucide-react';
import { Button, EmptyState, Input, Table, type TableColumn } from '../components/ui';
import { formatDate } from '../lib/financeFormat';
import { listStorageFiles, uploadStorageFile } from '../services/storageService';
import type { StorageFileDto } from '../types/schema';

export function StoragePage() {
  const [files, setFiles] = useState<StorageFileDto[]>([]);
  const [file, setFile] = useState<File | null>(null);
  const [storedFileName, setStoredFileName] = useState('');
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    reload();
  }, []);

  async function reload() {
    setFiles(await listStorageFiles());
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!file) {
      setMessage('Choose a file to store.');
      return;
    }

    setSaving(true);
    setMessage(null);
    try {
      await uploadStorageFile(file, storedFileName || file.name);
      setFile(null);
      setStoredFileName('');
      const input = document.getElementById('storage-file') as HTMLInputElement | null;
      if (input) {
        input.value = '';
      }
      await reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to store file.');
    } finally {
      setSaving(false);
    }
  }

  const columns: TableColumn<StorageFileDto>[] = [
    { key: 'storedFileName', label: 'Stored Name' },
    { key: 'originalFileName', label: 'Original Name' },
    { key: 'contentType', label: 'Type' },
    { key: 'sizeBytes', label: 'Size', align: 'right', render: (item) => `${Math.round(item.sizeBytes / 1024)} KB` },
    { key: 'createdAt', label: 'Stored', render: (item) => formatDate(item.createdAt) },
  ];

  return (
    <section className="page">
      <header className="page-header">
        <div>
          <h1 className="page-title">Storage Room</h1>
          <p className="page-subtitle">Store reusable import files before creating an import batch.</p>
        </div>
      </header>

      <form className="panel form-grid" onSubmit={submit}>
        <Input
          id="storage-file"
          label="File"
          type="file"
          onChange={(event) => {
            const nextFile = event.target.files?.[0] ?? null;
            setFile(nextFile);
            setStoredFileName(nextFile?.name ?? '');
          }}
          required
        />
        <Input label="Stored file name" value={storedFileName} onChange={(event) => setStoredFileName(event.target.value)} required />
        <Button type="submit" loading={saving} leftIcon={<Upload size={15} />}>Store File</Button>
        {message && <div className="notice error">{message}</div>}
      </form>

      <div className="panel">
        {files.length === 0 ? <EmptyState title="No stored files" message="Upload files here, then use them from the imports page." /> : (
          <Table data={files} columns={columns} rowKey="id" />
        )}
      </div>
    </section>
  );
}
