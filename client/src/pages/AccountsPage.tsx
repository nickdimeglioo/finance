import { FormEvent, useState } from 'react';
import { formatMoney } from '../lib/financeFormat';
import { createAccount, archiveAccount, useAccounts } from '../services/accountsService';
import type { AccountType, CreateAccountRequest } from '../types/schema';

const accountTypes: AccountType[] = ['checking', 'savings', 'credit_card', 'loan', 'investment', 'cash', 'other'];

export function AccountsPage() {
  const { accounts, loading, error, reload } = useAccounts();
  const [form, setForm] = useState<CreateAccountRequest>({
    nickname: '',
    institution: '',
    type: 'checking',
    currency: 'USD',
    openingBalance: 0,
    includeInDashboard: true,
  });
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setMessage(null);
    try {
      await createAccount(form);
      setForm({ nickname: '', institution: '', type: 'checking', currency: 'USD', openingBalance: 0, includeInDashboard: true });
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

  return (
    <section className="page">
      <header className="page-header">
        <div>
          <h1 className="page-title">Accounts</h1>
          <p className="page-subtitle">Balances, account status, and dashboard inclusion.</p>
        </div>
      </header>

      <form className="panel form-grid" onSubmit={submit}>
        <label>
          Nickname
          <input value={form.nickname} onChange={(event) => setForm({ ...form, nickname: event.target.value })} required />
        </label>
        <label>
          Institution
          <input value={form.institution ?? ''} onChange={(event) => setForm({ ...form, institution: event.target.value })} />
        </label>
        <label>
          Type
          <select value={form.type} onChange={(event) => setForm({ ...form, type: event.target.value as AccountType })}>
            {accountTypes.map((type) => (
              <option key={type} value={type}>
                {type}
              </option>
            ))}
          </select>
        </label>
        <label>
          Currency
          <input value={form.currency} maxLength={3} onChange={(event) => setForm({ ...form, currency: event.target.value.toUpperCase() })} />
        </label>
        <label>
          Opening balance
          <input
            type="number"
            step="0.01"
            min="0"
            value={form.openingBalance}
            onChange={(event) => setForm({ ...form, openingBalance: Number(event.target.value) })}
          />
        </label>
        <label className="checkbox-row">
          <input
            type="checkbox"
            checked={form.includeInDashboard}
            onChange={(event) => setForm({ ...form, includeInDashboard: event.target.checked })}
          />
          Include in dashboard
        </label>
        <button type="submit" disabled={saving}>{saving ? 'Saving...' : 'Add Account'}</button>
        {message && <div className="notice error">{message}</div>}
      </form>

      <div className="panel">
        {loading && <p>Loading accounts...</p>}
        {error && <p>{error.message}</p>}
        {!loading && !error && accounts.length === 0 && <p>No accounts yet.</p>}
        {accounts.length > 0 && (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Account</th>
                  <th>Type</th>
                  <th>Status</th>
                  <th>Balance</th>
                  <th>Dashboard</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {accounts.map((account) => (
                  <tr key={account.id}>
                    <td>
                      <strong>{account.nickname}</strong>
                      <div className="muted">{account.institution}</div>
                    </td>
                    <td>{account.type}</td>
                    <td>{account.status}</td>
                    <td>{formatMoney(account.currentBalance, account.currency)}</td>
                    <td>{account.includeInDashboard ? 'Yes' : 'No'}</td>
                    <td>
                      {account.status !== 'archived' && (
                        <button type="button" className="secondary" onClick={() => archive(account.id)}>
                          Archive
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </section>
  );
}

