import { EmptyState, Table, type TableColumn } from '../components/ui';
import { formatDate, formatMoney, monthRange } from '../lib/financeFormat';
import { useDashboardSummary } from '../services/dashboardService';
import type { TransactionListItemDto } from '../types/schema';

export function DashboardPage() {
  const currentMonth = monthRange();
  const { summary, loading, error } = useDashboardSummary(currentMonth.from, currentMonth.to);
  const columns: TableColumn<TransactionListItemDto>[] = [
    { key: 'date', label: 'Date', render: (transaction) => formatDate(transaction.date) },
    { key: 'description', label: 'Description' },
    { key: 'type', label: 'Type' },
    {
      key: 'amount',
      label: 'Amount',
      align: 'right',
      render: (transaction) => formatMoney(transaction.amount, transaction.currency)
    }
  ];

  return (
    <section className="page">
      <header className="page-header">
        <div>
          <h1 className="page-title">Dashboard</h1>
          <p className="page-subtitle">
            Current period: {currentMonth.from} to {currentMonth.to}
          </p>
        </div>
      </header>

      {error && <div className="notice error">{error.message}</div>}
      {loading && <div className="panel">Loading dashboard...</div>}

      <div className="metric-grid">
        <Metric label="Income" value={formatMoney(summary?.totalIncome ?? 0)} />
        <Metric label="Expenses" value={formatMoney(summary?.totalExpenses ?? 0)} />
        <Metric label="Net cash flow" value={formatMoney(summary?.netCashFlow ?? 0)} />
        <Metric label="Liquid balance" value={formatMoney(summary?.totalLiquidBalance ?? 0)} />
      </div>

      <div className="panel">
        <div className="panel-header">
          <h2>Recent Transactions</h2>
          <span>{summary?.pendingReminderCount ?? 0} reminders</span>
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
