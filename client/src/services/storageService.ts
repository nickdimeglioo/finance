import { apiRequest } from '../lib/http';
import type { StorageFileDto } from '../types/schema';

export function listStorageFiles(): Promise<StorageFileDto[]> {
  return apiRequest<StorageFileDto[]>('/storage-files');
}

export function uploadStorageFile(file: File, storedFileName: string): Promise<StorageFileDto> {
  const form = new FormData();
  form.set('storedFileName', storedFileName);
  form.set('file', file);
  return apiRequest<StorageFileDto>('/storage-files', {
    method: 'POST',
    body: form,
  });
}
