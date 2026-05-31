import { useEffect, useState } from 'react';
import { apiRequest } from '../lib/http';
import type { AccountDetailDto, AccountListItemDto, CreateAccountRequest, UpdateAccountRequest } from '../types/schema';

export function listAccounts(): Promise<AccountListItemDto[]> {
  return apiRequest<AccountListItemDto[]>('/accounts');
}

export function getAccount(id: string): Promise<AccountDetailDto> {
  return apiRequest<AccountDetailDto>(`/accounts/${id}`);
}

export function createAccount(request: CreateAccountRequest): Promise<AccountDetailDto> {
  return apiRequest<AccountDetailDto>('/accounts', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function updateAccount(id: string, request: UpdateAccountRequest): Promise<AccountDetailDto> {
  return apiRequest<AccountDetailDto>(`/accounts/${id}`, {
    method: 'PUT',
    body: JSON.stringify(request),
  });
}

export function archiveAccount(id: string): Promise<void> {
  return apiRequest<void>(`/accounts/${id}`, {
    method: 'DELETE',
  });
}

export function useAccounts() {
  const [accounts, setAccounts] = useState<AccountListItemDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    listAccounts()
      .then((data) => {
        if (!cancelled) {
          setAccounts(data);
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
  }, []);

  return { accounts, loading, error, reload: () => listAccounts().then(setAccounts) };
}
