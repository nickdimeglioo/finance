import { apiRequest } from '../lib/http';
import type {
  NoteDto,
  NoteMatchSuggestionDto,
  RecurringRuleDto,
  RecurringRuleSuggestionDto,
  ReminderDto,
  SubscriptionStatusDto,
  TagDto,
  UpsertNoteRequest,
  UpsertRecurringRuleRequest,
  UpsertTagRequest,
} from '../types/schema';

export const listTags = () => apiRequest<TagDto[]>('/tags');
export const createTag = (request: UpsertTagRequest) => apiRequest<TagDto>('/tags', { method: 'POST', body: JSON.stringify(request) });
export const updateTag = (id: string, request: UpsertTagRequest) => apiRequest<TagDto>(`/tags/${id}`, { method: 'PUT', body: JSON.stringify(request) });
export const deleteTag = (id: string) => apiRequest<void>(`/tags/${id}`, { method: 'DELETE' });

export const listRecurringRules = () => apiRequest<RecurringRuleDto[]>('/recurring-rules');
export const getSubscriptionStatus = () => apiRequest<SubscriptionStatusDto>('/subscriptions/status');
export const createRecurringRule = (request: UpsertRecurringRuleRequest) => apiRequest<RecurringRuleDto>('/recurring-rules', { method: 'POST', body: JSON.stringify(request) });
export const updateRecurringRule = (id: string, request: UpsertRecurringRuleRequest) => apiRequest<RecurringRuleDto>(`/recurring-rules/${id}`, { method: 'PUT', body: JSON.stringify(request) });
export const deleteRecurringRule = (id: string) => apiRequest<void>(`/recurring-rules/${id}`, { method: 'DELETE' });
export const matchRecurringRules = () => apiRequest<{ matched: number }>('/recurring-rules/match', { method: 'POST' });
export const listRecurringRuleSuggestions = () => apiRequest<RecurringRuleSuggestionDto[]>('/recurring-rules/suggestions');

export const listNotes = (status?: string) => apiRequest<NoteDto[]>(`/notes${status ? `?status=${encodeURIComponent(status)}` : ''}`);
export const createNote = (request: UpsertNoteRequest) => apiRequest<NoteDto>('/notes', { method: 'POST', body: JSON.stringify(request) });
export const updateNote = (id: string, request: UpsertNoteRequest) => apiRequest<NoteDto>(`/notes/${id}`, { method: 'PUT', body: JSON.stringify(request) });
export const deleteNote = (id: string) => apiRequest<void>(`/notes/${id}`, { method: 'DELETE' });
export const findNoteMatches = (transactionId: string) => apiRequest<NoteMatchSuggestionDto[]>('/notes/match', { method: 'POST', body: JSON.stringify({ transactionId }) });
export const acceptNoteMatch = (noteId: string, transactionId: string) => apiRequest<NoteDto>(`/notes/${noteId}/match`, { method: 'PATCH', body: JSON.stringify({ transactionId }) });
export const dismissNote = (id: string) => apiRequest<NoteDto>(`/notes/${id}/dismiss`, { method: 'PATCH' });

export const listReminders = (includeResolved = false) => apiRequest<ReminderDto[]>(`/reminders?includeResolved=${includeResolved}`);
export const dismissReminder = (id: string) => apiRequest<ReminderDto>(`/reminders/${id}/dismiss`, { method: 'PATCH' });
export const completeReminder = (id: string) => apiRequest<ReminderDto>(`/reminders/${id}/complete`, { method: 'PATCH' });
