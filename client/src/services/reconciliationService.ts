import { apiRequest } from '../lib/http';
import type { BalanceCheckpointDto, CreateBalanceCheckpointRequest, ReconcileAccountDto } from '../types/schema';

export const listCheckpoints = (accountId: string) => apiRequest<BalanceCheckpointDto[]>(`/accounts/${accountId}/checkpoints`);
export const createCheckpoint = (accountId: string, request: CreateBalanceCheckpointRequest) =>
  apiRequest<BalanceCheckpointDto>(`/accounts/${accountId}/checkpoints`, { method: 'POST', body: JSON.stringify(request) });
export const getReconcile = (accountId: string, from?: string, to?: string) => {
  const params = new URLSearchParams();
  if (from) params.set('from', from);
  if (to) params.set('to', to);
  const query = params.toString();
  return apiRequest<ReconcileAccountDto>(`/accounts/${accountId}/reconcile${query ? `?${query}` : ''}`);
};

