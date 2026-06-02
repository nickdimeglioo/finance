import { FormEvent, useState } from 'react';
import { Ban, Save } from 'lucide-react';
import { Button, EmptyState, Input, Select, StatusBadge, Table, type TableColumn } from '../components/ui';
import { displayEnum } from '../lib/display';
import { formatDate, formatMoney, toApiDate } from '../lib/financeFormat';
import { useAccounts } from '../services/accountsService';
import { createTransaction, createTransfer, updateTransactionStatus, useTransactions, voidTransaction } from '../services/transactionsService';
import type { CreateTransactionRequest, CreateTransferRequest, TransactionClassification, TransactionListItemDto, TransactionStatus, TransactionType } from '../types/schema';

const types: TransactionType[] = ['expense', 'income', 'adjustment'];
const classifications: TransactionClassification[] = ['personal', 'business', 'mixed', 'ignored', 'unknown'];
const statuses: TransactionStatus[] = ['pending', 'posted', 'reconciled', 'voided'];

export function TransactionsPage() {
  const { accounts } = useAccounts();
  const { result, loading, error, reload } = useTransactions({ page: 1, pageSize: 50 });
  const [mode, setMode] = useState<'transaction' | 'transfer'>('transaction');
  const [message, setMessage] = useState<string | null>(null);
  const [transaction, setTransaction] = useState<CreateTransactionRequest>({
    accountId: '',
    date: toApiDate(new Date()),
    description: '',
    type: 'expense',
    classification: 'personal',
    amount: 0,
    currency: 'USD',
  });
  const [transfer, setTransfer] = useState<CreateTransferRequest>({
    fromAccountId: '',
    toAccountId: '',
    date: toApiDate(new Date()),
    description: '',
    amount: 0,
    currency: 'USD',
    classification: 'personal',
  });

  async function submit(event: FormEvent) {
    event.preventDefault();
    setMessage(null);
    try {
      if (mode === 'transfer') {
        await createTransfer(transfer);
      } else {
        await createTransaction(transaction);
      }
      await reload();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to save transaction.');
    }
  }

  const columns: TableColumn<TransactionListItemDto>[] = [
    { key: 'date', label: 'Date', render: (item) => formatDate(item.date) },
    { key: 'description', label: 'Description' },
    { key: 'type', label: 'Type', render: (item) => displayEnum(item.type) },
    { key: 'classification', label: 'Class', render: (item) => displayEnum(item.classification) },
    {
      key: 'status',
      label: 'Status',
      render: (item) => (
        <Select
          aria-label="Transaction status"
          value={item.status}
          onChange={async (event) => {
            await updateTransactionStatus(item.id, event.target.value as TransactionStatus);
            await reload();
          }}
        >
          {statuses.map((status) => (
            <option key={status} value={status}>{displayEnum(status)}</option>
          ))}
        </Select>
      )
    },
    {
      key: 'amount',
      label: 'Amount',
      align: 'right',
      render: (item) => formatMoney(item.amount, item.currency)
    },
    {
      key: 'actions',
      label: '',
      align: 'right',
      render: (item) =>
        !item.isVoid ? (
          <Button
            type="button"
            variant="secondary"
            size="sm"
            leftIcon={<Ban size={15} />}
            onClick={async () => {
              await voidTransaction(item.id, item.type === 'transfer');
              await reload();
            }}
          >
            Void
          </Button>
        ) : (
          <StatusBadge label="Voided" tone="danger" />
        )
    }
  ];

  return (
    <section className="page">
      <header className="page-header">
        <div>
          <h1 className="page-title">Transactions</h1>
          <p className="page-subtitle">Manual entry, transfer pairing, voiding, and status changes.</p>
        </div>
      </header>

      <form className="panel form-grid" onSubmit={submit}>
        <Select label="Mode" value={mode} onChange={(event) => setMode(event.target.value as 'transaction' | 'transfer')}>
          <option value="transaction">Transaction</option>
          <option value="transfer">Transfer</option>
        </Select>

        {mode === 'transaction' ? (
          <>
            <Select label="Account" value={transaction.accountId} onChange={(event) => setTransaction({ ...transaction, accountId: event.target.value })} required>
              <option value="">Select account</option>
              {accounts.map((account) => (
                <option key={account.id} value={account.id}>{account.nickname}</option>
              ))}
            </Select>
            <Select label="Type" value={transaction.type} onChange={(event) => setTransaction({ ...transaction, type: event.target.value as TransactionType })}>
              {types.map((type) => <option key={type} value={type}>{displayEnum(type)}</option>)}
            </Select>
          </>
        ) : (
          <>
            <Select label="From" value={transfer.fromAccountId} onChange={(event) => setTransfer({ ...transfer, fromAccountId: event.target.value })} required>
              <option value="">Select account</option>
              {accounts.map((account) => <option key={account.id} value={account.id}>{account.nickname}</option>)}
            </Select>
            <Select label="To" value={transfer.toAccountId} onChange={(event) => setTransfer({ ...transfer, toAccountId: event.target.value })} required>
              <option value="">Select account</option>
              {accounts.map((account) => <option key={account.id} value={account.id}>{account.nickname}</option>)}
            </Select>
          </>
        )}

        <Input
          label="Date"
          type="date"
          value={mode === 'transfer' ? transfer.date : transaction.date}
          onChange={(event) => mode === 'transfer' ? setTransfer({ ...transfer, date: event.target.value }) : setTransaction({ ...transaction, date: event.target.value })}
        />
        <Input
          label="Description"
          value={mode === 'transfer' ? transfer.description : transaction.description}
          onChange={(event) => mode === 'transfer' ? setTransfer({ ...transfer, description: event.target.value }) : setTransaction({ ...transaction, description: event.target.value })}
          required
        />
        <Input
          label="Amount"
          type="number"
          step="0.01"
          value={mode === 'transfer' ? transfer.amount : transaction.amount}
          onChange={(event) => mode === 'transfer' ? setTransfer({ ...transfer, amount: Number(event.target.value) }) : setTransaction({ ...transaction, amount: Number(event.target.value) })}
        />
        <Select
          label="Classification"
          value={mode === 'transfer' ? transfer.classification : transaction.classification}
          onChange={(event) => mode === 'transfer'
            ? setTransfer({ ...transfer, classification: event.target.value as TransactionClassification })
            : setTransaction({ ...transaction, classification: event.target.value as TransactionClassification })}
        >
          {classifications.map((classification) => <option key={classification} value={classification}>{displayEnum(classification)}</option>)}
        </Select>
        <Button type="submit" leftIcon={<Save size={15} />}>Save</Button>
        {message && <div className="notice error">{message}</div>}
      </form>

      <div className="panel">
        {error && <p>{error.message}</p>}
        {!error && result.items.length === 0 && !loading ? <EmptyState title="No transactions yet." /> : null}
        {!error && (result.items.length > 0 || loading) ? (
          <Table data={result.items} columns={columns} rowKey="id" loading={loading} emptyMessage="No transactions yet." />
        ) : null}
      </div>
    </section>
  );
}
