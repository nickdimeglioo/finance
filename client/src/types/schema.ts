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
  subcategory?: string | null;
  tags: string[];
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
  uniqueId?: string | null;
  rulesetId?: string | null;
  rulesetVersion?: number | null;
  matchedClassificationRuleId?: string | null;
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
  pendingReminders: ReminderDto[];
}

export interface TagDto {
  id: string;
  name: string;
  color?: string | null;
  transactionCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface UpsertTagRequest {
  name: string;
  color?: string | null;
}

export type RecurringFrequency = 'daily' | 'weekly' | 'biweekly' | 'monthly' | 'quarterly' | 'yearly';

export interface RecurringRuleDto {
  id: string;
  name: string;
  accountId?: string | null;
  type: string;
  classification: TransactionClassification;
  amount: number;
  currency: string;
  category?: string | null;
  merchantKeyword?: string | null;
  frequency: RecurringFrequency;
  nextExpected: string;
  lastMatchedDate?: string | null;
  amountTolerance: number;
  tags: string[];
  isActive: boolean;
  status: 'overdue' | 'upcoming' | 'on_track';
  monthlyNormalizedCost: number;
  createdAt: string;
  updatedAt: string;
}

export interface UpsertRecurringRuleRequest {
  name: string;
  accountId?: string | null;
  type: string;
  classification: TransactionClassification;
  amount: number;
  currency: string;
  category?: string | null;
  merchantKeyword?: string | null;
  frequency: RecurringFrequency;
  nextExpected: string;
  amountTolerance: number;
  tags?: string[] | null;
  isActive: boolean;
}

export interface SubscriptionStatusDto {
  monthlyTotal: number;
  businessMonthlyTotal: number;
  personalMonthlyTotal: number;
  rules: RecurringRuleDto[];
}

export interface RecurringRuleSuggestionDto {
  accountId: string;
  name: string;
  type: string;
  classification: TransactionClassification;
  amount: number;
  currency: string;
  category?: string | null;
  merchantKeyword: string;
  frequency: RecurringFrequency;
  nextExpected: string;
  occurrences: number;
}

export interface NoteDto {
  id: string;
  title: string;
  body?: string | null;
  amountHint?: number | null;
  merchantHint?: string | null;
  dateHint?: string | null;
  matchedTransactionId?: string | null;
  status: 'unmatched' | 'matched' | 'dismissed';
  remindOn?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface UpsertNoteRequest {
  title: string;
  body?: string | null;
  amountHint?: number | null;
  merchantHint?: string | null;
  dateHint?: string | null;
  remindOn?: string | null;
}

export interface NoteMatchSuggestionDto {
  note: NoteDto;
  score: number;
  reasons: string[];
}

export interface ReminderDto {
  id: string;
  type: 'note' | 'recurring_rule' | 'reconciliation' | 'custom';
  sourceId?: string | null;
  title: string;
  message?: string | null;
  dueOn: string;
  status: 'pending' | 'dismissed' | 'completed';
  createdAt: string;
  updatedAt: string;
}

export interface CashFlowPointDto {
  month: string;
  income: number;
  expenses: number;
  netCashFlow: number;
  cumulativeCashFlow: number;
}

export interface BreakdownItemDto {
  label: string;
  amount: number;
  percentage: number;
}

export interface BusinessPersonalSummaryDto {
  businessExpenses: number;
  personalExpenses: number;
  mixedExpenses: number;
  ignoredExpenses: number;
  unknownExpenses: number;
  items: BreakdownItemDto[];
}

export interface BalanceHistoryPointDto {
  date: string;
  balance: number;
}

export interface NetWorthPointDto {
  date: string;
  netWorth: number;
}

export interface ExportRequest {
  exportType: 'transactions' | 'report';
  from?: string | null;
  to?: string | null;
  accountId?: string | null;
  classification?: string | null;
}

export interface ExportFileDto {
  id: string;
  exportType: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  createdAt: string;
  expiresAt?: string | null;
  downloadUrl: string;
}

export interface BalanceCheckpointDto {
  id: string;
  accountId: string;
  date: string;
  balance: number;
  notes?: string | null;
  expectedBalance: number;
  discrepancy: number;
  createdAt: string;
  updatedAt: string;
}

export interface CreateBalanceCheckpointRequest {
  date: string;
  balance: number;
  notes?: string | null;
}

export interface ReconcileAccountDto {
  accountId: string;
  from: string;
  to: string;
  openingClearedBalance: number;
  closingExpectedBalance: number;
  unreconciledTransactions: TransactionListItemDto[];
}

export interface UserFinanceProfileDto {
  id: string;
  dateOfBirth?: string | null;
  annualIncome?: number | null;
  incomeType: 'salaried' | 'freelance' | 'mixed' | 'retired' | 'student' | 'other';
  dependents: number;
  financialGoals: string[];
  categoryMappings: Record<string, 'needs' | 'wants' | 'savings'>;
  createdAt: string;
  updatedAt: string;
}

export interface UpdateUserFinanceProfileRequest {
  dateOfBirth?: string | null;
  annualIncome?: number | null;
  incomeType: UserFinanceProfileDto['incomeType'];
  dependents: number;
  financialGoals?: string[] | null;
  categoryMappings?: Record<string, 'needs' | 'wants' | 'savings'> | null;
}

export interface GuidanceItemDto {
  id: string;
  title: string;
  status: 'on_track' | 'below_target' | 'warning' | 'no_data';
  message: string;
  supportingMetrics: Record<string, number>;
}

export interface GuidanceSummaryDto {
  from: string;
  to: string;
  guidance: GuidanceItemDto[];
}

export interface CategoryDto {
  name: string;
  transactionCount: number;
  splitCount: number;
}

export type ImportStatus = 'uploaded' | 'parsed' | 'previewed' | 'committed' | 'failed' | 'cancelled';

export interface ImportBatchDto {
  id: string;
  accountId: string;
  institution?: string | null;
  originalFileName: string;
  contentType: string;
  s3ObjectKey: string;
  status: ImportStatus;
  rowCount: number;
  acceptedCount: number;
  duplicateCount: number;
  errorCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface UploadImportResponse {
  id: string;
  accountId: string;
  originalFileName: string;
  contentType: string;
  s3ObjectKey: string;
  status: ImportStatus;
}

export interface ImportTemplateDto {
  id: string;
  institution?: string | null;
  name: string;
  columnMap: Record<string, string>;
  dateFormat?: string | null;
  amountFormat?: string | null;
}

export interface ParsedImportDto {
  batchId: string;
  columns: string[];
  sampleRows: Record<string, string>[];
  templates: ImportTemplateDto[];
}

export interface ImportColumnMap {
  date?: string | null;
  description?: string | null;
  merchant?: string | null;
  amount?: string | null;
  debit?: string | null;
  credit?: string | null;
  type?: string | null;
  category?: string | null;
  classification?: string | null;
}

export interface PreviewImportRequest {
  columnMap: ImportColumnMap;
  ruleSetId?: string | null;
  dateFormat?: string | null;
  amountFormat?: string | null;
  saveTemplate: boolean;
  templateName?: string | null;
}

export interface ImportPreviewRowDto {
  id: string;
  rowNumber: number;
  rawData: Record<string, string>;
  rawDescription?: string | null;
  cleanedDescription?: string | null;
  date?: string | null;
  amount?: number | null;
  type?: TransactionType | null;
  category?: string | null;
  classification: TransactionClassification;
  importHash?: string | null;
  isDuplicate: boolean;
  accepted: boolean;
  errors: string[];
}

export interface UpdateImportPreviewRowRequest {
  cleanedDescription?: string | null;
  date?: string | null;
  amount?: number | null;
  type?: string | null;
  category?: string | null;
  classification?: string | null;
  accepted?: boolean | null;
}

export interface ImportCommitResult {
  imported: number;
  skippedDuplicates: number;
  rejected: number;
  errors: number;
}

export interface ImportRuleDto {
  id: string;
  ruleSetId?: string | null;
  name: string;
  pattern?: string | null;
  sourceField?: string | null;
  targetField?: string | null;
  valueTransform: string;
  mapsToType?: string | null;
  mapsToCategory?: string | null;
  mapsToClassification?: string | null;
  mapsToDescription?: string | null;
  priority: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface UpsertImportRuleRequest {
  ruleSetId?: string | null;
  name: string;
  pattern?: string | null;
  sourceField?: string | null;
  targetField?: string | null;
  valueTransform?: string | null;
  mapsToType?: string | null;
  mapsToCategory?: string | null;
  mapsToClassification?: string | null;
  mapsToDescription?: string | null;
  priority: number;
  isActive: boolean;
}

export interface TestImportRuleResult {
  rawDescription: string;
  cleanedDescription: string;
  type?: string | null;
  category?: string | null;
  classification: TransactionClassification;
  matchedRules: ImportRuleDto[];
}

export interface ImportRuleSetDto {
  id: string;
  name: string;
  institution?: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface ClassificationRuleDto {
  id: string;
  name: string;
  ruleType: string;
  fieldTarget: string;
  value: string;
  classification: TransactionClassification;
  alsoSetCategory?: string | null;
  priority: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface UpsertClassificationRuleRequest {
  name: string;
  ruleType: string;
  fieldTarget: string;
  value: string;
  classification: TransactionClassification;
  alsoSetCategory?: string | null;
  priority: number;
  isActive: boolean;
}

export interface TestClassificationRuleResult {
  classification: TransactionClassification;
  category?: string | null;
  matchedRule?: ClassificationRuleDto | null;
}

export interface StorageFileDto {
  id: string;
  originalFileName: string;
  storedFileName: string;
  contentType: string;
  s3ObjectKey: string;
  sizeBytes: number;
  purpose: string;
  createdAt: string;
  updatedAt: string;
}

export interface RulesetSourceConfigDto {
  delimiter?: string | null;
  encoding?: string | null;
  expectedColumns?: string[] | null;
}

export interface RulesetRulesDocumentDto {
  onError?: string | null;
  fallback?: RulesetFallbackDto | null;
  rules?: RulesetRuleDefinitionDto[] | null;
}

export interface RulesetRuleDefinitionDto {
  id: string;
  name?: string | null;
  priority: number;
  kind: 'field' | 'enrichment' | string;
  isActive: boolean;
  target?: string | null;
  match?: ClassificationConditionDto | null;
  flow?: RulesetRuleStepDto[] | null;
  output?: RulesetRuleOutputDto | null;
}

export interface RulesetRuleStepDto {
  when?: ClassificationConditionDto | null;
  source?: string | null;
  value?: unknown;
  transform?: MappingTransformDto | null;
  expr?: string | null;
}

export interface MappingTransformDto {
  type: string;
  format?: string | null;
  value?: string | null;
}

export interface RulesetFallbackDto {
  merchant?: string | null;
  category?: string | null;
  subcategory?: string | null;
  classification?: TransactionClassification | null;
  tags?: string[] | null;
}

export interface ClassificationConditionDto {
  op: string;
  field?: string | null;
  value?: unknown;
  conditions?: ClassificationConditionDto[] | null;
}

export interface RulesetRuleOutputDto {
  merchant?: string | null;
  category?: string | null;
  subcategory?: string | null;
  classification?: TransactionClassification | null;
  tags?: string[] | null;
}

export interface RulesetDto {
  id: string;
  name: string;
  description?: string | null;
  version: number;
  sourceConfig: RulesetSourceConfigDto;
  rules: RulesetRulesDocumentDto;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface UpsertRulesetRequest {
  name: string;
  description?: string | null;
  sourceConfig?: RulesetSourceConfigDto | null;
  rules?: RulesetRulesDocumentDto | null;
  isActive: boolean;
}

export interface ImportJobErrorDto {
  row: number;
  column?: string | null;
  message: string;
}

export interface ImportJobDto {
  id: string;
  accountId: string;
  rulesetId: string;
  rulesetVersion: number;
  fileName: string;
  status: 'pending' | 'processing' | 'complete' | 'failed';
  totalRows: number;
  successRows: number;
  skippedRows: number;
  errorRows: number;
  errors: ImportJobErrorDto[];
  isDryRun: boolean;
  startedAt: string;
  completedAt?: string | null;
}

export interface RulesetImportPreviewRowDto {
  rowNumber: number;
  rawRow: Record<string, string>;
  date?: string | null;
  amount?: number | null;
  type?: TransactionType | null;
  description?: string | null;
  merchant?: string | null;
  category?: string | null;
  subcategory?: string | null;
  classification?: TransactionClassification | null;
  tags: string[];
  uniqueId?: string | null;
  isDuplicate: boolean;
  accepted: boolean;
  matchedRuleIds: string[];
  errors: ImportJobErrorDto[];
}

export interface RulesetImportResult {
  job: ImportJobDto;
  previewRows: RulesetImportPreviewRowDto[];
}

export interface RulesetImportRowOverride {
  rowNumber: number;
  date?: string | null;
  amount?: number | null;
  type?: TransactionType | null;
  description?: string | null;
  merchant?: string | null;
  category?: string | null;
  subcategory?: string | null;
  classification?: TransactionClassification | null;
  tags?: string[] | null;
}

export interface RulesetImportCommitOptions {
  acceptedRowNumbers: number[];
  rowOverrides: RulesetImportRowOverride[];
}
