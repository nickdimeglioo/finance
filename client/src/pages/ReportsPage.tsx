import { FormEvent, useEffect, useMemo, useState } from 'react';
import { Download, RotateCw } from 'lucide-react';
import { Button, Checkbox, EmptyState, Input, Select, StatusBadge, Table, type TableColumn } from '../components/ui';
import { displayEnum } from '../lib/display';
import { formatDate, formatMoney, monthRange, toApiDate } from '../lib/financeFormat';
import { useAccounts } from '../services/accountsService';
import { createCheckpoint, getReconcile, listCheckpoints } from '../services/reconciliationService';
import {
  createTransactionsExport,
  downloadExportFile,
  getBalanceHistory,
  getBusinessPersonal,
  getCashFlow,
  getCategoryBreakdown,
  getNetWorth,
  getTagBreakdown,
  listExports,
} from '../services/reportsService';
import { updateTransactionStatus } from '../services/transactionsService';
import type {
  BalanceCheckpointDto,
  BalanceHistoryPointDto,
  BreakdownItemDto,
  BusinessPersonalSummaryDto,
  CashFlowPointDto,
  ExportFileDto,
  NetWorthPointDto,
  ReconcileAccountDto,
  TransactionListItemDto,
} from '../types/schema';

type Tab = 'cashFlow' | 'categories' | 'reconciliation' | 'exports';

function lastTwelveMonths(): { from: string; to: string } {
  const now = new Date();
  return {
    from: toApiDate(new Date(now.getFullYear(), now.getMonth() - 11, 1)),
    to: toApiDate(new Date(now.getFullYear(), now.getMonth() + 1, 0)),
  };
}

export function ReportsPage() {
  const { accounts } = useAccounts();
  const currentMonth = monthRange();
  const [tab, setTab] = useState<Tab>('cashFlow');
  const [from, setFrom] = useState(currentMonth.from);
  const [to, setTo] = useState(currentMonth.to);
  const [classification, setClassification] = useState('');
  const [exportAccountIds, setExportAccountIds] = useState<string[]>([]);
  const [cashFlow, setCashFlow] = useState<CashFlowPointDto[]>([]);
  const [categories, setCategories] = useState<BreakdownItemDto[]>([]);
  const [tags, setTags] = useState<BreakdownItemDto[]>([]);
  const [business, setBusiness] = useState<BusinessPersonalSummaryDto | null>(null);
  const [netWorth, setNetWorth] = useState<NetWorthPointDto[]>([]);
  const [exports, setExports] = useState<ExportFileDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [message, setMessage] = useState<string | null>(null);

  async function loadReports() {
    setLoading(true);
    setMessage(null);
    try {
      const [cash, categoryData, tagData, businessData, netWorthData, exportData] = await Promise.all([
        getCashFlow(6),
        getCategoryBreakdown(from, to, classification),
        getTagBreakdown(from, to),
        getBusinessPersonal(from, to),
        getNetWorth(from, to),
        listExports(),
      ]);
      setCashFlow(cash);
      setCategories(categoryData);
      setTags(tagData);
      setBusiness(businessData);
      setNetWorth(netWorthData);
      setExports(exportData);
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to load reports.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void loadReports();
  }, [from, to, classification]);

  useEffect(() => {
    if (accounts.length === 0) {
      return;
    }

    setExportAccountIds((current) => {
      const available = new Set(accounts.map((account) => account.id));
      const retained = current.filter((id) => available.has(id));
      return retained.length > 0 ? retained : accounts.map((account) => account.id);
    });
  }, [accounts]);

  async function exportTransactions() {
    setMessage(null);
    try {
      const created = await createTransactionsExport({
        exportType: 'transactions',
        from,
        to,
        accountIds: exportAccountIds,
        classification: classification || null,
      });
      setExports([created, ...exports]);
      await downloadExportFile(created);
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to export transactions.');
    }
  }

  async function downloadStoredExport(file: ExportFileDto) {
    setMessage(null);
    try {
      await downloadExportFile(file);
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to download export.');
    }
  }

  function toggleExportAccount(accountId: string, checked: boolean) {
    setExportAccountIds((current) => checked
      ? [...current, accountId]
      : current.filter((id) => id !== accountId));
  }

  return (
    <section className="page">
      <header className="page-header">
        <div>
          <h1 className="page-title">Reports</h1>
          <p className="page-subtitle">Server-side aggregations, CSV exports, balance history, and reconciliation.</p>
        </div>
        <Button type="button" variant="secondary" leftIcon={<RotateCw size={16} />} onClick={loadReports}>Refresh</Button>
      </header>

      {message && <div className="notice error">{message}</div>}

      <div className="panel form-grid">
        <Select label="Period" value={`${from}|${to}`} onChange={(event) => {
          const value = event.target.value;
          if (value === 'current') {
            const range = monthRange();
            setFrom(range.from);
            setTo(range.to);
          } else if (value === 'last12') {
            const range = lastTwelveMonths();
            setFrom(range.from);
            setTo(range.to);
          }
        }}>
          <option value={`${from}|${to}`}>Custom</option>
          <option value="current">This month</option>
          <option value="last12">Last 12 months</option>
        </Select>
        <Input label="From" type="date" value={from} onChange={(event) => setFrom(event.target.value)} />
        <Input label="To" type="date" value={to} onChange={(event) => setTo(event.target.value)} />
        <Select label="Classification" value={classification} onChange={(event) => setClassification(event.target.value)}>
          <option value="">All</option>
          <option value="personal">Personal</option>
          <option value="business">Business</option>
          <option value="transfer">Transfer</option>
          <option value="investment">Investment</option>
          <option value="tax">Tax</option>
          <option value="reimbursement">Reimbursement</option>
          <option value="exclude">Exclude</option>
          <option value="mixed">Mixed</option>
          <option value="ignored">Ignored</option>
          <option value="unknown">Unknown</option>
        </Select>
        <div className="field-span">
          <div className="panel-header compact-header">
            <span className="pm-label">Export accounts</span>
            <div className="row-actions">
              <Button type="button" size="sm" variant="secondary" onClick={() => setExportAccountIds(accounts.map((account) => account.id))}>All</Button>
              <Button type="button" size="sm" variant="secondary" onClick={() => setExportAccountIds([])}>Clear</Button>
            </div>
          </div>
          <div className="selector-grid">
            {accounts.map((account) => (
              <Checkbox
                key={account.id}
                label={account.nickname}
                checked={exportAccountIds.includes(account.id)}
                onChange={(event) => toggleExportAccount(account.id, event.target.checked)}
              />
            ))}
          </div>
        </div>
        <Button type="button" leftIcon={<Download size={16} />} onClick={exportTransactions} disabled={exportAccountIds.length === 0}>Export CSV</Button>
      </div>

      <div className="tabs">
        {(['cashFlow', 'categories', 'reconciliation', 'exports'] as Tab[]).map((item) => (
          <button key={item} type="button" className={tab === item ? 'tab active' : 'tab'} onClick={() => setTab(item)}>
            {displayEnum(item)}
          </button>
        ))}
      </div>

      {tab === 'cashFlow' && <CashFlowSection loading={loading} cashFlow={cashFlow} netWorth={netWorth} />}
      {tab === 'categories' && <BreakdownSection loading={loading} categories={categories} tags={tags} business={business} />}
      {tab === 'reconciliation' && <ReconciliationSection from={from} to={to} />}
      {tab === 'exports' && <ExportsSection exports={exports} loading={loading} onDownload={downloadStoredExport} />}
    </section>
  );
}

function CashFlowSection({ loading, cashFlow, netWorth }: { loading: boolean; cashFlow: CashFlowPointDto[]; netWorth: NetWorthPointDto[] }) {
  const max = Math.max(1, ...cashFlow.flatMap((row) => [row.income, row.expenses]).map(Math.abs));
  return (
    <>
      <div className="metric-grid">
        <Metric label="Income" value={formatMoney(cashFlow.reduce((sum, row) => sum + row.income, 0))} />
        <Metric label="Expenses" value={formatMoney(cashFlow.reduce((sum, row) => sum + row.expenses, 0))} />
        <Metric label="Net cash flow" value={formatMoney(cashFlow.reduce((sum, row) => sum + row.netCashFlow, 0))} />
        <Metric label="Latest net worth" value={formatMoney(netWorth.at(-1)?.netWorth ?? 0)} />
      </div>
      <div className="panel">
        <div className="panel-header"><h2>Income vs Expense</h2></div>
        {loading ? <p>Loading report...</p> : cashFlow.length === 0 ? <EmptyState title="No cash flow data." /> : (
          <div className="bar-chart">
            {cashFlow.map((row) => (
              <div key={row.month} className="bar-row">
                <span>{row.month}</span>
                <div className="bar-track"><i className="bar income" style={{ width: `${Math.max(2, row.income / max * 100)}%` }} /></div>
                <div className="bar-track"><i className="bar expense" style={{ width: `${Math.max(2, row.expenses / max * 100)}%` }} /></div>
                <strong>{formatMoney(row.netCashFlow)}</strong>
              </div>
            ))}
          </div>
        )}
      </div>
    </>
  );
}

function BreakdownSection({ loading, categories, tags, business }: { loading: boolean; categories: BreakdownItemDto[]; tags: BreakdownItemDto[]; business: BusinessPersonalSummaryDto | null }) {
  const columns: TableColumn<BreakdownItemDto>[] = [
    { key: 'label', label: 'Label', render: (item) => displayEnum(item.label) },
    { key: 'amount', label: 'Amount', align: 'right', render: (item) => formatMoney(item.amount) },
    { key: 'percentage', label: 'Share', align: 'right', render: (item) => `${item.percentage.toFixed(1)}%` },
  ];
  return (
    <div className="split-panel">
      <div className="metric-grid">
        <Metric label="Business" value={formatMoney(business?.businessExpenses ?? 0)} />
        <Metric label="Personal" value={formatMoney(business?.personalExpenses ?? 0)} />
        <Metric label="Mixed" value={formatMoney(business?.mixedExpenses ?? 0)} />
        <Metric label="Unknown" value={formatMoney(business?.unknownExpenses ?? 0)} />
      </div>
      <div className="panel">
        <div className="panel-header"><h2>Category Breakdown</h2></div>
        <Table data={categories} columns={columns} rowKey="label" loading={loading} emptyMessage="No category spend in this period." />
      </div>
      <div className="panel">
        <div className="panel-header"><h2>Tag Breakdown</h2></div>
        <Table data={tags} columns={columns} rowKey="label" loading={loading} emptyMessage="No tagged spend in this period." />
      </div>
    </div>
  );
}

function ReconciliationSection({ from, to }: { from: string; to: string }) {
  const { accounts } = useAccounts();
  const [accountId, setAccountId] = useState('');
  const [history, setHistory] = useState<BalanceHistoryPointDto[]>([]);
  const [checkpoints, setCheckpoints] = useState<BalanceCheckpointDto[]>([]);
  const [reconcile, setReconcile] = useState<ReconcileAccountDto | null>(null);
  const [balance, setBalance] = useState(0);
  const [notes, setNotes] = useState('');

  const selectedAccount = useMemo(() => accounts.find((account) => account.id === accountId), [accounts, accountId]);

  useEffect(() => {
    if (!accountId && accounts[0]) {
      setAccountId(accounts[0].id);
    }
  }, [accounts, accountId]);

  useEffect(() => {
    if (!accountId) return;
    void Promise.all([getBalanceHistory(accountId, 12), listCheckpoints(accountId), getReconcile(accountId, from, to)]).then(([historyData, checkpointData, reconcileData]) => {
      setHistory(historyData);
      setCheckpoints(checkpointData);
      setReconcile(reconcileData);
    });
  }, [accountId, from, to]);

  async function submitCheckpoint(event: FormEvent) {
    event.preventDefault();
    if (!accountId) return;
    const checkpoint = await createCheckpoint(accountId, { date: to, balance, notes });
    setCheckpoints([checkpoint, ...checkpoints]);
    setBalance(0);
    setNotes('');
  }

  async function markReconciled(id: string) {
    await updateTransactionStatus(id, 'reconciled');
    if (accountId) {
      setReconcile(await getReconcile(accountId, from, to));
    }
  }

  const checkpointColumns: TableColumn<BalanceCheckpointDto>[] = [
    { key: 'date', label: 'Date', render: (item) => formatDate(item.date) },
    { key: 'balance', label: 'Reported', align: 'right', render: (item) => formatMoney(item.balance, selectedAccount?.currency) },
    { key: 'expectedBalance', label: 'Expected', align: 'right', render: (item) => formatMoney(item.expectedBalance, selectedAccount?.currency) },
    { key: 'discrepancy', label: 'Discrepancy', align: 'right', render: (item) => <StatusBadge label={formatMoney(item.discrepancy, selectedAccount?.currency)} tone={Math.abs(item.discrepancy) < 0.01 ? 'success' : 'warning'} /> },
  ];
  const transactionColumns: TableColumn<TransactionListItemDto>[] = [
    { key: 'date', label: 'Date', render: (item) => formatDate(item.date) },
    { key: 'description', label: 'Description' },
    { key: 'amount', label: 'Amount', align: 'right', render: (item) => formatMoney(item.amount, item.currency) },
    { key: 'actions', label: '', align: 'right', render: (item) => <Button type="button" size="sm" onClick={() => markReconciled(item.id)}>Reconcile</Button> },
  ];

  return (
    <div className="split-panel">
      <div className="panel form-grid">
        <Select label="Account" value={accountId} onChange={(event) => setAccountId(event.target.value)}>
          {accounts.map((account) => <option key={account.id} value={account.id}>{account.nickname}</option>)}
        </Select>
        <Metric label="Opening cleared" value={formatMoney(reconcile?.openingClearedBalance ?? 0, selectedAccount?.currency)} />
        <Metric label="Expected closing" value={formatMoney(reconcile?.closingExpectedBalance ?? 0, selectedAccount?.currency)} />
      </div>
      <div className="panel">
        <div className="panel-header"><h2>Balance History</h2></div>
        <div className="sparkline">
          {history.map((point) => <span key={point.date} title={`${point.date} ${point.balance}`} style={{ height: `${Math.max(6, Math.abs(point.balance) / Math.max(1, ...history.map((item) => Math.abs(item.balance))) * 90)}%` }} />)}
        </div>
      </div>
      <form className="panel form-grid" onSubmit={submitCheckpoint}>
        <Input label="Checkpoint date" type="date" value={to} readOnly />
        <Input label="Reported balance" type="number" step="0.01" value={balance} onChange={(event) => setBalance(Number(event.target.value))} />
        <Input label="Notes" value={notes} onChange={(event) => setNotes(event.target.value)} />
        <Button type="submit">Add Checkpoint</Button>
      </form>
      <div className="panel">
        <div className="panel-header"><h2>Checkpoints</h2></div>
        <Table data={checkpoints} columns={checkpointColumns} rowKey="id" emptyMessage="No checkpoints yet." />
      </div>
      <div className="panel">
        <div className="panel-header"><h2>Unreconciled Transactions</h2></div>
        <Table data={reconcile?.unreconciledTransactions ?? []} columns={transactionColumns} rowKey="id" emptyMessage="No unreconciled transactions in this period." />
      </div>
    </div>
  );
}

function ExportsSection({ exports, loading, onDownload }: { exports: ExportFileDto[]; loading: boolean; onDownload: (file: ExportFileDto) => void }) {
  const columns: TableColumn<ExportFileDto>[] = [
    { key: 'fileName', label: 'File' },
    { key: 'exportType', label: 'Type', render: (item) => displayEnum(item.exportType) },
    { key: 'createdAt', label: 'Created', render: (item) => formatDate(item.createdAt) },
    { key: 'sizeBytes', label: 'Size', align: 'right', render: (item) => `${Math.ceil(item.sizeBytes / 1024)} KB` },
    { key: 'actions', label: '', align: 'right', render: (item) => <Button type="button" size="sm" variant="secondary" leftIcon={<Download size={14} />} onClick={() => onDownload(item)}>Download</Button> },
  ];
  return (
    <div className="panel">
      <div className="panel-header"><h2>Stored Exports</h2></div>
      <Table data={exports} columns={columns} rowKey="id" loading={loading} emptyMessage="No stored exports yet." />
    </div>
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
