export type AccountType = 'checking' | 'savings' | 'credit_card' | 'loan' | 'investment' | 'cash' | 'other';
export type AccountStatus = 'active' | 'archived' | 'closed';
export type TransactionType = 'income' | 'expense' | 'transfer' | 'adjustment' | 'opening_balance';
export type TransactionClassification = 'business' | 'personal' | 'mixed' | 'ignored' | 'unknown';
export type TransactionStatus = 'pending' | 'posted' | 'reconciled' | 'voided';
export type TransactionSource = 'manual' | 'import' | 'system';
export type TransactionDirection = 'inflow' | 'outflow' | 'neutral';

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface AccountListItemDto {
  id: string;
  institution?: string | null;
  nickname: string;
  type: AccountType;
  currency: string;
  currentBalance: number;
  status: AccountStatus;
  includeInDashboard: boolean;
}

export interface AccountDetailDto extends AccountListItemDto {
  openingBalance: number;
  creditLimit?: number | null;
  interestRate?: number | null;
  notes?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateAccountRequest {
  institution?: string | null;
  nickname: string;
  type: AccountType;
  currency: string;
  openingBalance: number;
  creditLimit?: number | null;
  interestRate?: number | null;
  notes?: string | null;
  includeInDashboard: boolean;
}

export interface UpdateAccountRequest extends CreateAccountRequest {
  status: AccountStatus;
}

export interface TransactionListItemDto {
  id: string;
  accountId: string;
  date: string;
  postedAt?: string | null;
  description: string;
  merchant?: string | null;
  type: TransactionType;
  classification: TransactionClassification;
  category?: string | null;
  amount: number;
  currency: string;
  direction: TransactionDirection;
  status: TransactionStatus;
  source: TransactionSource;
  isVoid: boolean;
  isSplit: boolean;
  transferPartnerId?: string | null;
}

export interface TransactionSplitDto {
  id: string;
  category: string;
  classification: TransactionClassification;
  amount: number;
  notes?: string | null;
}

export interface TransactionDetailDto extends TransactionListItemDto {
  importHash?: string | null;
  recurringRuleId?: string | null;
  splits: TransactionSplitDto[];
  createdAt: string;
  updatedAt: string;
}

export interface CreateTransactionSplitRequest {
  category: string;
  classification: TransactionClassification;
  amount: number;
  notes?: string | null;
}

export interface CreateTransactionRequest {
  accountId: string;
  date: string;
  postedAt?: string | null;
  description: string;
  merchant?: string | null;
  type: TransactionType;
  classification: TransactionClassification;
  category?: string | null;
  amount: number;
  currency: string;
  splits?: CreateTransactionSplitRequest[] | null;
}

export interface CreateTransferRequest {
  fromAccountId: string;
  toAccountId: string;
  date: string;
  postedAt?: string | null;
  description: string;
  amount: number;
  currency: string;
  classification: TransactionClassification;
  category?: string | null;
}

export interface UpdateTransactionRequest extends CreateTransactionRequest {
  status: TransactionStatus;
}

export interface TransactionFiltersRequest {
  accountId?: string;
  type?: TransactionType;
  classification?: TransactionClassification;
  category?: string;
  status?: TransactionStatus;
  tagId?: string;
  from?: string;
  to?: string;
  amountMin?: number;
  amountMax?: number;
  search?: string;
  includeVoided?: boolean;
  page?: number;
  pageSize?: number;
}

export interface CurrentUserDto {
  id: string;
  email?: string | null;
}

export interface DashboardSummaryDto {
  from: string;
  to: string;
  totalIncome: number;
  totalExpenses: number;
  netCashFlow: number;
  totalLiquidBalance: number;
  recentTransactions: TransactionListItemDto[];
  pendingReminderCount: number;
}
