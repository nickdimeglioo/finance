import { apiRequest } from '../lib/http';
import type {
  BalanceHistoryPointDto,
  BreakdownItemDto,
  BusinessPersonalSummaryDto,
  CashFlowPointDto,
  ExportFileDto,
  ExportRequest,
  NetWorthPointDto,
} from '../types/schema';

function query(params: Record<string, string | number | undefined | null>): string {
  const search = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== '') {
      search.set(key, String(value));
    }
  });
  const value = search.toString();
  return value ? `?${value}` : '';
}

export const getCashFlow = (months = 6) => apiRequest<CashFlowPointDto[]>(`/reports/cash-flow?months=${months}`);
export const getCategoryBreakdown = (from?: string, to?: string, classification?: string) =>
  apiRequest<BreakdownItemDto[]>(`/reports/category-breakdown${query({ from, to, classification })}`);
export const getBusinessPersonal = (from?: string, to?: string) =>
  apiRequest<BusinessPersonalSummaryDto>(`/reports/business-personal${query({ from, to })}`);
export const getTagBreakdown = (from?: string, to?: string) =>
  apiRequest<BreakdownItemDto[]>(`/reports/tag-breakdown${query({ from, to })}`);
export const getNetWorth = (from?: string, to?: string) => apiRequest<NetWorthPointDto[]>(`/reports/net-worth${query({ from, to })}`);
export const getBalanceHistory = (accountId: string, months = 12) => apiRequest<BalanceHistoryPointDto[]>(`/accounts/${accountId}/balance-history?months=${months}`);
export const listExports = () => apiRequest<ExportFileDto[]>('/exports');
export const createTransactionsExport = (request: ExportRequest) => apiRequest<ExportFileDto>('/exports/transactions', { method: 'POST', body: JSON.stringify(request) });

