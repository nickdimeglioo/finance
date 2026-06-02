import { apiRequest } from '../lib/http';
import type {
  ClassificationRuleDto,
  ImportBatchDto,
  ImportCommitResult,
  ImportPreviewRowDto,
  ImportRuleDto,
  ImportRuleSetDto,
  ParsedImportDto,
  PreviewImportRequest,
  TestClassificationRuleResult,
  TestImportRuleResult,
  UpdateImportPreviewRowRequest,
  UploadImportResponse,
  UpsertClassificationRuleRequest,
  UpsertImportRuleRequest,
} from '../types/schema';

export function listImports(): Promise<ImportBatchDto[]> {
  return apiRequest<ImportBatchDto[]>('/imports');
}

export function uploadImport(accountId: string, institution: string, file: File): Promise<UploadImportResponse> {
  const form = new FormData();
  form.set('accountId', accountId);
  form.set('institution', institution);
  form.set('file', file);
  return apiRequest<UploadImportResponse>('/imports/upload', {
    method: 'POST',
    body: form,
  });
}

export function createImportFromStorage(accountId: string, institution: string, fileName: string): Promise<UploadImportResponse> {
  return apiRequest<UploadImportResponse>('/imports/from-storage', {
    method: 'POST',
    body: JSON.stringify({ accountId, institution, fileName }),
  });
}

export function parseImport(batchId: string): Promise<ParsedImportDto> {
  return apiRequest<ParsedImportDto>(`/imports/${batchId}/parse`, { method: 'POST' });
}

export function previewImport(batchId: string, request: PreviewImportRequest): Promise<ImportPreviewRowDto[]> {
  return apiRequest<ImportPreviewRowDto[]>(`/imports/${batchId}/preview`, {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function getImportPreview(batchId: string): Promise<ImportPreviewRowDto[]> {
  return apiRequest<ImportPreviewRowDto[]>(`/imports/${batchId}/preview`);
}

export function updatePreviewRow(batchId: string, rowId: string, request: UpdateImportPreviewRowRequest): Promise<ImportPreviewRowDto> {
  return apiRequest<ImportPreviewRowDto>(`/imports/${batchId}/preview-rows/${rowId}`, {
    method: 'PUT',
    body: JSON.stringify(request),
  });
}

export function commitImport(batchId: string): Promise<ImportCommitResult> {
  return apiRequest<ImportCommitResult>(`/imports/${batchId}/commit`, { method: 'POST' });
}

export function listImportRules(): Promise<ImportRuleDto[]> {
  return apiRequest<ImportRuleDto[]>('/import-rules');
}

export function listImportRuleSets(): Promise<ImportRuleSetDto[]> {
  return apiRequest<ImportRuleSetDto[]>('/import-rules/sets');
}

export function createImportRuleSet(name: string, institution?: string): Promise<ImportRuleSetDto> {
  return apiRequest<ImportRuleSetDto>('/import-rules/sets', {
    method: 'POST',
    body: JSON.stringify({ name, institution, isActive: true }),
  });
}

export function createImportRule(request: UpsertImportRuleRequest): Promise<ImportRuleDto> {
  return apiRequest<ImportRuleDto>('/import-rules', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function updateImportRule(id: string, request: UpsertImportRuleRequest): Promise<ImportRuleDto> {
  return apiRequest<ImportRuleDto>(`/import-rules/${id}`, {
    method: 'PUT',
    body: JSON.stringify(request),
  });
}

export function deleteImportRule(id: string): Promise<void> {
  return apiRequest<void>(`/import-rules/${id}`, { method: 'DELETE' });
}

export function testImportRule(rawDescription: string): Promise<TestImportRuleResult> {
  return apiRequest<TestImportRuleResult>('/import-rules/test', {
    method: 'POST',
    body: JSON.stringify({ rawDescription }),
  });
}

export function listClassificationRules(): Promise<ClassificationRuleDto[]> {
  return apiRequest<ClassificationRuleDto[]>('/classification-rules');
}

export function createClassificationRule(request: UpsertClassificationRuleRequest): Promise<ClassificationRuleDto> {
  return apiRequest<ClassificationRuleDto>('/classification-rules', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function testClassificationRule(description: string, amount: number): Promise<TestClassificationRuleResult> {
  return apiRequest<TestClassificationRuleResult>('/classification-rules/test', {
    method: 'POST',
    body: JSON.stringify({ description, amount }),
  });
}
