import { apiRequest } from '../lib/http';
import type { BudgetGoalDto, BudgetGoalKind, UpsertBudgetGoalRequest } from '../types/schema';

export const listBudgetGoals = (kind?: BudgetGoalKind) =>
  apiRequest<BudgetGoalDto[]>(`/budget-goals${kind ? `?kind=${encodeURIComponent(kind)}` : ''}`);

export const createBudgetGoal = (request: UpsertBudgetGoalRequest) =>
  apiRequest<BudgetGoalDto>('/budget-goals', { method: 'POST', body: JSON.stringify(request) });

export const updateBudgetGoal = (id: string, request: UpsertBudgetGoalRequest) =>
  apiRequest<BudgetGoalDto>(`/budget-goals/${id}`, { method: 'PUT', body: JSON.stringify(request) });

export const deleteBudgetGoal = (id: string) =>
  apiRequest<void>(`/budget-goals/${id}`, { method: 'DELETE' });
