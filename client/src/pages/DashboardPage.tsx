import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Button, Checkbox, EmptyState, Input, Table, type TableColumn } from '../components/ui';
import { displayEnum } from '../lib/display';
import { formatDate, formatMoney, monthRange } from '../lib/financeFormat';
import { useAccounts } from '../services/accountsService';
import { useDashboardSummary } from '../services/dashboardService';
import { completeReminder, dismissReminder } from '../services/organizationService';
import type { ReminderDto, TransactionListItemDto } from '../types/schema';

export function DashboardPage() {
  const currentMonth = monthRange();
  const { accounts } = useAccounts();
  const [selectedAccountIds, setSelectedAccountIds] = useState<string[]>([]);
  const [from, setFrom] = useState(currentMonth.from);
  const [to, setTo] = useState(currentMonth.to);
  const { summary, loading, error } = useDashboardSummary(from, to, selectedAccountIds);
  const [hiddenReminderIds, setHiddenReminderIds] = useState<string[]>([]);
  const columns: TableColumn<TransactionListItemDto>[] = [
    { key: 'date', label: 'Date', render: (transaction) => formatDate(transaction.date) },
    { key: 'description', label: 'Description' },
    { key: 'type', label: 'Type', render: (transaction) => displayEnum(transaction.type) },
    {
      key: 'amount',
      label: 'Amount',
      align: 'right',
      render: (transaction) => formatMoney(transaction.amount, transaction.currency)
    }
  ];
  const reminderColumns: TableColumn<ReminderDto>[] = [
    { key: 'dueOn', label: 'Due', render: (reminder) => formatDate(reminder.dueOn) },
    { key: 'title', label: 'Reminder', render: (reminder) => <div><strong>{reminder.title}</strong>{reminder.message && <div className="muted">{reminder.message}</div>}</div> },
    { key: 'type', label: 'Type', render: (reminder) => displayEnum(reminder.type) },
    { key: 'actions', label: '', align: 'right', render: (reminder) => <div className="row-actions">
      <Link className="table-resource-link" to={reminder.type === 'note' ? '/notes' : reminder.type === 'recurring_rule' ? '/subscriptions' : '/dashboard'}>View</Link>
      <Button type="button" size="sm" onClick={async () => { await completeReminder(reminder.id); setHiddenReminderIds([...hiddenReminderIds, reminder.id]); }}>Complete</Button>
      <Button type="button" size="sm" variant="secondary" onClick={async () => { await dismissReminder(reminder.id); setHiddenReminderIds([...hiddenReminderIds, reminder.id]); }}>Dismiss</Button>
    </div> },
  ];

  return (
    <section className="page">
      <header className="page-header">
        <div>
          <h1 className="page-title">Dashboard</h1>
          <p className="page-subtitle">
            Current period: {from} to {to}. Committed imports update totals when their transaction dates fall inside this range.
          </p>
        </div>
      </header>

      {error && <div className="notice error">{error.message}</div>}
      {loading && <div className="panel">Loading dashboard...</div>}

      <div className="panel form-grid">
        <Input label="From" type="date" value={from} onChange={(event) => setFrom(event.target.value)} />
        <Input label="To" type="date" value={to} onChange={(event) => setTo(event.target.value)} />
        <Button type="button" variant="secondary" onClick={() => {
          const range = monthRange();
          setFrom(range.from);
          setTo(range.to);
        }}>Current Month</Button>
      </div>

      <div className="panel account-selector">
        <div className="panel-header">
          <h2>Dashboard Accounts</h2>
          <button type="button" className="link-button" onClick={() => setSelectedAccountIds([])}>All accounts</button>
        </div>
        <div className="selector-grid">
          {accounts.map((account) => (
            <Checkbox
              key={account.id}
              label={`${account.nickname} · ${displayEnum(account.type)}`}
              checked={selectedAccountIds.includes(account.id)}
              onChange={(event) => {
                setSelectedAccountIds((current) =>
                  event.target.checked
                    ? [...current, account.id]
                    : current.filter((id) => id !== account.id));
              }}
            />
          ))}
        </div>
      </div>

      <div className="metric-grid">
        <Metric label="Income" value={formatMoney(summary?.totalIncome ?? 0)} />
        <Metric label="Expenses" value={formatMoney(summary?.totalExpenses ?? 0)} />
        <Metric label="Net cash flow" value={formatMoney(summary?.netCashFlow ?? 0)} />
        <Metric label="Liquid balance" value={formatMoney(summary?.totalLiquidBalance ?? 0)} />
      </div>

      <div className="panel">
        <div className="panel-header">
          <h2>Pending Reminders</h2>
          <span>{(summary?.pendingReminders ?? []).filter((item) => !hiddenReminderIds.includes(item.id)).length}</span>
        </div>
        {summary && summary.pendingReminders.filter((item) => !hiddenReminderIds.includes(item.id)).length === 0 && <EmptyState title="No pending reminders." />}
        {summary && summary.pendingReminders.filter((item) => !hiddenReminderIds.includes(item.id)).length > 0 && <Table data={summary.pendingReminders.filter((item) => !hiddenReminderIds.includes(item.id))} columns={reminderColumns} rowKey="id" />}
      </div>

      <div className="panel">
        <div className="panel-header">
          <h2>Recent Transactions</h2>
          <span>{summary?.recentTransactions.length ?? 0}</span>
        </div>
        {summary && summary.recentTransactions.length === 0 && <EmptyState title="No recent transactions." />}
        {summary && summary.recentTransactions.length > 0 && (
          <Table data={summary.recentTransactions} columns={columns} rowKey="id" />
        )}
      </div>
    </section>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="metric">
      <div className="metric-label">{label}</div>
      <div className="metric-value">{value}</div>
    </div>
  );
}
