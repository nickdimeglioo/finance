import { apiRequest } from '../lib/http';
import type { GuidanceSummaryDto } from '../types/schema';

export const getGuidance = () => apiRequest<GuidanceSummaryDto>('/guidance');

