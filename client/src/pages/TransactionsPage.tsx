import { FormEvent, useState } from 'react';
import { formatDate, formatMoney, toApiDate } from '../lib/financeFormat';
import { useAccounts } from '../services/accountsService';
import { createTransaction, createTransfer, updateTransactionStatus, useTransactions, voidTransaction } from '../services/transactionsService';
import type { CreateTransactionRequest, CreateTransferRequest, TransactionClassification, TransactionType } from '../types/schema';

const types: TransactionType[] = ['expense', 'income', 'adjustment'];
const classifications: TransactionClassification[] = ['personal', 'business', 'mixed', 'ignored', 'unknown'];

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

  return (
    <section className="page">
      <header className="page-header">
        <div>
          <h1 className="page-title">Transactions</h1>
          <p className="page-subtitle">Manual entry, transfer pairing, voiding, and status changes.</p>
        </div>
      </header>

      <form className="panel form-grid" onSubmit={submit}>
        <label>
          Mode
          <select value={mode} onChange={(event) => setMode(event.target.value as 'transaction' | 'transfer')}>
            <option value="transaction">Transaction</option>
            <option value="transfer">Transfer</option>
          </select>
        </label>

        {mode === 'transaction' ? (
          <>
            <label>
              Account
              <select value={transaction.accountId} onChange={(event) => setTransaction({ ...transaction, accountId: event.target.value })} required>
                <option value="">Select account</option>
                {accounts.map((account) => (
                  <option key={account.id} value={account.id}>{account.nickname}</option>
                ))}
              </select>
            </label>
            <label>
              Type
              <select value={transaction.type} onChange={(event) => setTransaction({ ...transaction, type: event.target.value as TransactionType })}>
                {types.map((type) => <option key={type} value={type}>{type}</option>)}
              </select>
            </label>
          </>
        ) : (
          <>
            <label>
              From
              <select value={transfer.fromAccountId} onChange={(event) => setTransfer({ ...transfer, fromAccountId: event.target.value })} required>
                <option value="">Select account</option>
                {accounts.map((account) => <option key={account.id} value={account.id}>{account.nickname}</option>)}
              </select>
            </label>
            <label>
              To
              <select value={transfer.toAccountId} onChange={(event) => setTransfer({ ...transfer, toAccountId: event.target.value })} required>
                <option value="">Select account</option>
                {accounts.map((account) => <option key={account.id} value={account.id}>{account.nickname}</option>)}
              </select>
            </label>
          </>
        )}

        <label>
          Date
          <input
            type="date"
            value={mode === 'transfer' ? transfer.date : transaction.date}
            onChange={(event) => mode === 'transfer' ? setTransfer({ ...transfer, date: event.target.value }) : setTransaction({ ...transaction, date: event.target.value })}
          />
        </label>
        <label>
          Description
          <input
            value={mode === 'transfer' ? transfer.description : transaction.description}
            onChange={(event) => mode === 'transfer' ? setTransfer({ ...transfer, description: event.target.value }) : setTransaction({ ...transaction, description: event.target.value })}
            required
          />
        </label>
        <label>
          Amount
          <input
            type="number"
            step="0.01"
            value={mode === 'transfer' ? transfer.amount : transaction.amount}
            onChange={(event) => mode === 'transfer' ? setTransfer({ ...transfer, amount: Number(event.target.value) }) : setTransaction({ ...transaction, amount: Number(event.target.value) })}
          />
        </label>
        <label>
          Classification
          <select
            value={mode === 'transfer' ? transfer.classification : transaction.classification}
            onChange={(event) => mode === 'transfer'
              ? setTransfer({ ...transfer, classification: event.target.value as TransactionClassification })
              : setTransaction({ ...transaction, classification: event.target.value as TransactionClassification })}
          >
            {classifications.map((classification) => <option key={classification} value={classification}>{classification}</option>)}
          </select>
        </label>
        <button type="submit">Save</button>
        {message && <div className="notice error">{message}</div>}
      </form>

      <div className="panel">
        {loading && <p>Loading transactions...</p>}
        {error && <p>{error.message}</p>}
        {!loading && !error && result.items.length === 0 && <p>No transactions yet.</p>}
        {result.items.length > 0 && (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Description</th>
                  <th>Type</th>
                  <th>Class</th>
                  <th>Status</th>
                  <th>Amount</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {result.items.map((item) => (
                  <tr key={item.id}>
                    <td>{formatDate(item.date)}</td>
                    <td>{item.description}</td>
                    <td>{item.type}</td>
                    <td>{item.classification}</td>
                    <td>
                      <select value={item.status} onChange={async (event) => { await updateTransactionStatus(item.id, event.target.value); await reload(); }}>
                        <option value="pending">pending</option>
                        <option value="posted">posted</option>
                        <option value="reconciled">reconciled</option>
                        <option value="voided">voided</option>
                      </select>
                    </td>
                    <td>{formatMoney(item.amount, item.currency)}</td>
                    <td>
                      {!item.isVoid && (
                        <button type="button" className="secondary" onClick={async () => { await voidTransaction(item.id, item.type === 'transfer'); await reload(); }}>
                          Void
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

