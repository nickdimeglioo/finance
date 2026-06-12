import { environment } from '../config/environment';
import { apiRequest } from '../lib/http';
import type { ReceiptAttachmentDto, ReceiptMatchSuggestionDto, UpdateReceiptAttachmentRequest } from '../types/schema';

export function listReceipts(status?: string, transactionId?: string): Promise<ReceiptAttachmentDto[]> {
  const params = new URLSearchParams();
  if (status) params.set('status', status);
  if (transactionId) params.set('transactionId', transactionId);
  const query = params.toString();
  return apiRequest<ReceiptAttachmentDto[]>(`/receipts${query ? `?${query}` : ''}`);
}

export function uploadReceipt(file: File, fields: {
  title: string;
  notes?: string | null;
  amountHint?: number | null;
  merchantHint?: string | null;
  dateHint?: string | null;
  transactionId?: string | null;
}): Promise<ReceiptAttachmentDto> {
  const form = new FormData();
  form.set('title', fields.title);
  if (fields.notes) form.set('notes', fields.notes);
  if (fields.amountHint !== null && fields.amountHint !== undefined) form.set('amountHint', String(fields.amountHint));
  if (fields.merchantHint) form.set('merchantHint', fields.merchantHint);
  if (fields.dateHint) form.set('dateHint', fields.dateHint);
  if (fields.transactionId) form.set('transactionId', fields.transactionId);
  form.set('file', file);
  return apiRequest<ReceiptAttachmentDto>('/receipts', { method: 'POST', body: form });
}

export const updateReceipt = (id: string, request: UpdateReceiptAttachmentRequest) =>
  apiRequest<ReceiptAttachmentDto>(`/receipts/${id}`, { method: 'PUT', body: JSON.stringify(request) });

export const deleteReceipt = (id: string) =>
  apiRequest<void>(`/receipts/${id}`, { method: 'DELETE' });

export const findReceiptMatches = (transactionId: string) =>
  apiRequest<ReceiptMatchSuggestionDto[]>('/receipts/match', { method: 'POST', body: JSON.stringify({ transactionId }) });

export const acceptReceiptMatch = (receiptId: string, transactionId: string) =>
  apiRequest<ReceiptAttachmentDto>(`/receipts/${receiptId}/match`, { method: 'PATCH', body: JSON.stringify({ transactionId }) });

export const dismissReceipt = (id: string) =>
  apiRequest<ReceiptAttachmentDto>(`/receipts/${id}/dismiss`, { method: 'PATCH' });

export const receiptDownloadUrl = (id: string) => `${environment.apiUrl}/receipts/${id}/download`;
