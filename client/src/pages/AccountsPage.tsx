import { FormEvent, useState } from 'react';
import { Archive } from 'lucide-react';
import { Button, EmptyState, Input, Select, StatusBadge, Table, type TableColumn } from '../components/ui';
import { displayEnum } from '../lib/display';
import { formatMoney } from '../lib/financeFormat';
import { createAccount, archiveAccount, useAccounts } from '../services/accountsService';
import type { AccountListItemDto, AccountType, CreateAccountRequest } from '../types/schema';

const accountTypes: AccountType[] = ['checking', 'savings', 'credit_card', 'loan', 'investment', 'cash', 'other'];

export function AccountsPage() {
  const { accounts, loading, error, reload } = useAccounts();
  const [form, setForm] = useState<CreateAccountRequest>({
    nickname: '',
    institution: '',
    type: 'checking',
    currency: 'USD',
    openingBalance: 0,
  });
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setMessage(null);
    try {
      await createAccount(form);
      setForm({ nickname: '', institution: '', type: 'checking', currency: 'USD', openingBalance: 0 });
      await reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to create account.');
    } finally {
      setSaving(false);
    }
  }

  async function archive(id: string) {
    await archiveAccount(id);
    await reload();
  }

  const columns: TableColumn<AccountListItemDto>[] = [
    {
      key: 'nickname',
      label: 'Account',
      render: (account) => (
        <>
          <strong>{account.nickname}</strong>
          <div className="muted">{account.institution}</div>
        </>
      )
    },
    { key: 'type', label: 'Type', render: (account) => displayEnum(account.type) },
    {
      key: 'status',
      label: 'Status',
      render: (account) => (
        <StatusBadge label={displayEnum(account.status)} tone={account.status === 'active' ? 'success' : 'neutral'} />
      )
    },
    {
      key: 'currentBalance',
      label: 'Balance',
      align: 'right',
      render: (account) => formatMoney(account.currentBalance, account.currency)
    },
    {
      key: 'actions',
      label: '',
      align: 'right',
      render: (account) =>
        account.status !== 'archived' ? (
          <Button type="button" variant="secondary" size="sm" leftIcon={<Archive size={15} />} onClick={() => archive(account.id)}>
            Archive
          </Button>
        ) : null
    }
  ];

  return (
    <section className="page">
      <header className="page-header">
        <div>
          <h1 className="page-title">Accounts</h1>
          <p className="page-subtitle">Balances and account status.</p>
        </div>
      </header>

      <form className="panel form-grid" onSubmit={submit}>
        <Input label="Nickname" value={form.nickname} onChange={(event) => setForm({ ...form, nickname: event.target.value })} required />
        <Input label="Institution" value={form.institution ?? ''} onChange={(event) => setForm({ ...form, institution: event.target.value })} />
        <Select label="Type" value={form.type} onChange={(event) => setForm({ ...form, type: event.target.value as AccountType })}>
          {accountTypes.map((type) => (
            <option key={type} value={type}>
              {displayEnum(type)}
            </option>
          ))}
        </Select>
        <Input label="Currency" value={form.currency} maxLength={3} onChange={(event) => setForm({ ...form, currency: event.target.value.toUpperCase() })} />
        <Input
          label="Opening balance"
          type="number"
          step="0.01"
          min="0"
          value={form.openingBalance}
          onChange={(event) => setForm({ ...form, openingBalance: Number(event.target.value) })}
        />
        <Button type="submit" loading={saving}>Add Account</Button>
        {message && <div className="notice error">{message}</div>}
      </form>

      <div className="panel">
        {error && <p>{error.message}</p>}
        {!error && accounts.length === 0 && !loading ? <EmptyState title="No accounts yet." /> : null}
        {!error && (accounts.length > 0 || loading) ? (
          <Table data={accounts} columns={columns} rowKey="id" loading={loading} emptyMessage="No accounts yet." />
        ) : null}
      </div>
    </section>
  );
}
