import { apiRequest } from '../lib/http';
import type { CategoryDto, UpdateUserFinanceProfileRequest, UserFinanceProfileDto } from '../types/schema';

export const getProfile = () => apiRequest<UserFinanceProfileDto>('/profile');
export const updateProfile = (request: UpdateUserFinanceProfileRequest) => apiRequest<UserFinanceProfileDto>('/profile', { method: 'PUT', body: JSON.stringify(request) });
export const listCategories = () => apiRequest<CategoryDto[]>('/categories');
export const renameCategory = (from: string, to: string) => apiRequest<{ updated: number }>('/categories/rename', { method: 'PATCH', body: JSON.stringify({ from, to }) });
