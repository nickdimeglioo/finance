import { useEffect, useState } from 'react';
import { apiRequest } from '../lib/http';
import { monthRange } from '../lib/financeFormat';
import type { DashboardSummaryDto } from '../types/schema';

export function getDashboardSummary(from?: string, to?: string): Promise<DashboardSummaryDto> {
  const range = from && to ? { from, to } : monthRange();
  return apiRequest<DashboardSummaryDto>(`/dashboard/summary?from=${range.from}&to=${range.to}`);
}

export function useDashboardSummary(from?: string, to?: string) {
  const [summary, setSummary] = useState<DashboardSummaryDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    getDashboardSummary(from, to)
      .then((data) => {
        if (!cancelled) {
          setSummary(data);
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
  }, [from, to]);

  return { summary, loading, error };
}

