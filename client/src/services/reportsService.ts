import { apiRequest } from '../lib/http';
import { environment } from '../config/environment';
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

export async function downloadExportFile(file: ExportFileDto): Promise<void> {
  const response = await fetch(toApiDownloadUrl(file.downloadUrl), { credentials: 'include' });
  if (!response.ok) {
    throw new Error(await response.text() || response.statusText);
  }

  const blob = await response.blob();
  const objectUrl = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = objectUrl;
  link.download = file.fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(objectUrl);
}

function toApiDownloadUrl(downloadUrl: string): string {
  if (/^https?:\/\//i.test(downloadUrl)) {
    return downloadUrl;
  }

  if (downloadUrl.startsWith('/api/v1/')) {
    return `${environment.apiUrl.replace(/\/api\/v1\/?$/, '')}${downloadUrl}`;
  }

  return `${environment.apiUrl}${downloadUrl.startsWith('/') ? downloadUrl : `/${downloadUrl}`}`;
}
