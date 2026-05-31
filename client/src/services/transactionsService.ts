import { useEffect, useMemo, useState } from 'react';
import { apiRequest } from '../lib/http';
import type {
  CreateTransactionRequest,
  CreateTransferRequest,
  PagedResult,
  TransactionDetailDto,
  TransactionFiltersRequest,
  TransactionListItemDto,
  UpdateTransactionRequest,
} from '../types/schema';

function toQuery(filters: TransactionFiltersRequest): string {
  const params = new URLSearchParams();
  Object.entries(filters).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== '') {
      params.set(key, String(value));
    }
  });
  const query = params.toString();
  return query ? `?${query}` : '';
}

export function listTransactions(filters: TransactionFiltersRequest = {}): Promise<PagedResult<TransactionListItemDto>> {
  return apiRequest<PagedResult<TransactionListItemDto>>(`/transactions${toQuery(filters)}`);
}

export function getTransaction(id: string): Promise<TransactionDetailDto> {
  return apiRequest<TransactionDetailDto>(`/transactions/${id}`);
}

export function createTransaction(request: CreateTransactionRequest): Promise<TransactionDetailDto> {
  return apiRequest<TransactionDetailDto>('/transactions', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function updateTransaction(id: string, request: UpdateTransactionRequest): Promise<TransactionDetailDto> {
  return apiRequest<TransactionDetailDto>(`/transactions/${id}`, {
    method: 'PUT',
    body: JSON.stringify(request),
  });
}

export function createTransfer(request: CreateTransferRequest): Promise<TransactionDetailDto> {
  return apiRequest<TransactionDetailDto>('/transactions/transfer', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function voidTransaction(id: string, includeTransferPartner = false): Promise<void> {
  return apiRequest<void>(`/transactions/${id}/void?includeTransferPartner=${includeTransferPartner}`, {
    method: 'PATCH',
  });
}

export function updateTransactionStatus(id: string, status: string): Promise<TransactionDetailDto> {
  return apiRequest<TransactionDetailDto>(`/transactions/${id}/status`, {
    method: 'PATCH',
    body: JSON.stringify({ status }),
  });
}

export function useTransactions(filters: TransactionFiltersRequest = {}) {
  const stableFilters = useMemo(() => JSON.stringify(filters), [filters]);
  const [result, setResult] = useState<PagedResult<TransactionListItemDto>>({
    items: [],
    page: filters.page ?? 1,
    pageSize: filters.pageSize ?? 50,
    totalCount: 0,
  });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    listTransactions(JSON.parse(stableFilters) as TransactionFiltersRequest)
      .then((data) => {
        if (!cancelled) {
          setResult(data);
          setError(null);
        }
      })
      .catch((err: Error) => {
        if (!cancelled) {
          setError(err);
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [stableFilters]);

  return { result, loading, error, reload: () => listTransactions(JSON.parse(stableFilters) as TransactionFiltersRequest).then(setResult) };
}

