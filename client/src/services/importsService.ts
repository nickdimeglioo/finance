import { apiRequest } from '../lib/http';
import type {
  ClassificationRuleDto,
  ImportBatchDto,
  ImportCommitResult,
  ImportPreviewRowDto,
  ImportJobDto,
  ImportRuleDto,
  ImportRuleSetDto,
  ParsedImportDto,
  PreviewImportRequest,
  RulesetDto,
  RulesetImportResult,
  TestClassificationRuleResult,
  TestImportRuleResult,
  UpdateImportPreviewRowRequest,
  UploadImportResponse,
  UpsertRulesetRequest,
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

export function deleteClassificationRule(id: string): Promise<void> {
  return apiRequest<void>(`/classification-rules/${id}`, { method: 'DELETE' });
}

export function testClassificationRule(description: string, amount: number): Promise<TestClassificationRuleResult> {
  return apiRequest<TestClassificationRuleResult>('/classification-rules/test', {
    method: 'POST',
    body: JSON.stringify({ description, amount }),
  });
}

export function listRulesets(): Promise<RulesetDto[]> {
  return apiRequest<RulesetDto[]>('/rulesets');
}

export function getRuleset(id: string): Promise<RulesetDto> {
  return apiRequest<RulesetDto>(`/rulesets/${id}`);
}

export function createRuleset(request: UpsertRulesetRequest): Promise<RulesetDto> {
  return apiRequest<RulesetDto>('/rulesets', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function updateRuleset(id: string, request: UpsertRulesetRequest): Promise<RulesetDto> {
  return apiRequest<RulesetDto>(`/rulesets/${id}`, {
    method: 'PUT',
    body: JSON.stringify(request),
  });
}

export function deleteRuleset(id: string): Promise<void> {
  return apiRequest<void>(`/rulesets/${id}`, { method: 'DELETE' });
}

export function importRulesetJson(ruleset: RulesetDto): Promise<RulesetDto> {
  return apiRequest<RulesetDto>('/rulesets/import-json', {
    method: 'POST',
    body: JSON.stringify({ ruleset }),
  });
}

export function exportRuleset(id: string): Promise<RulesetDto> {
  return apiRequest<RulesetDto>(`/rulesets/${id}/export`);
}

export function previewRulesetImport(accountId: string, rulesetId: string, file: File, deduplicationStrategy = 'skip'): Promise<RulesetImportResult> {
  return runRulesetImportRequest('/import/preview', accountId, rulesetId, file, deduplicationStrategy);
}

export function runRulesetImport(accountId: string, rulesetId: string, file: File, deduplicationStrategy = 'skip'): Promise<RulesetImportResult> {
  return runRulesetImportRequest('/import/run', accountId, rulesetId, file, deduplicationStrategy);
}

export function getImportJob(jobId: string): Promise<ImportJobDto> {
  return apiRequest<ImportJobDto>(`/import/jobs/${jobId}`);
}

function runRulesetImportRequest(path: string, accountId: string, rulesetId: string, file: File, deduplicationStrategy: string): Promise<RulesetImportResult> {
  const form = new FormData();
  form.set('accountId', accountId);
  form.set('rulesetId', rulesetId);
  form.set('deduplicationStrategy', deduplicationStrategy);
  form.set('file', file);
  return apiRequest<RulesetImportResult>(path, {
    method: 'POST',
    body: form,
  });
}
