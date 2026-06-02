import { FormEvent, useEffect, useMemo, useState } from 'react';
import { Check, FlaskConical, Pencil, Play, Trash2, Upload } from 'lucide-react';
import { Button, Checkbox, EmptyState, Input, Select, StatusBadge, Table, type TableColumn } from '../components/ui';
import { displayEnum } from '../lib/display';
import { formatDate, formatMoney } from '../lib/financeFormat';
import { useAccounts } from '../services/accountsService';
import {
  commitImport,
  createClassificationRule,
  createImportFromStorage,
  createImportRule,
  createImportRuleSet,
  deleteImportRule,
  listClassificationRules,
  listImportRules,
  listImportRuleSets,
  parseImport,
  previewImport,
  testClassificationRule,
  testImportRule,
  updateImportRule,
  updatePreviewRow,
  uploadImport,
} from '../services/importsService';
import { listStorageFiles } from '../services/storageService';
import type {
  ClassificationRuleDto,
  ImportColumnMap,
  ImportCommitResult,
  ImportPreviewRowDto,
  ImportRuleDto,
  ImportRuleSetDto,
  ParsedImportDto,
  PreviewImportRequest,
  StorageFileDto,
  TestClassificationRuleResult,
  TestImportRuleResult,
  TransactionClassification,
  TransactionType,
  UpsertImportRuleRequest,
} from '../types/schema';

const classifications: TransactionClassification[] = ['personal', 'business', 'mixed', 'ignored', 'unknown'];
const transactionTypes: TransactionType[] = ['expense', 'income', 'transfer', 'adjustment', 'opening_balance'];
const targetFields = ['date', 'description', 'merchant', 'amount', 'type', 'category', 'classification'];
const valueTransforms = ['copy', 'amount_positive', 'amount_negative'];
const emptyMap: ImportColumnMap = {
  date: '',
  description: '',
  merchant: '',
  amount: '',
  debit: '',
  credit: '',
  type: '',
  category: '',
  classification: '',
};

const emptyImportRuleForm = {
  ruleSetId: '',
  name: '',
  pattern: '',
  sourceField: '',
  targetField: '',
  valueTransform: 'copy',
  mapsToDescription: '',
  mapsToCategory: '',
  mapsToType: '',
  mapsToClassification: '',
  priority: 100,
  isActive: true,
};

export function ImportsPage() {
  const { accounts } = useAccounts();
  const [accountId, setAccountId] = useState('');
  const [institution, setInstitution] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [storedFileName, setStoredFileName] = useState('');
  const [storageFiles, setStorageFiles] = useState<StorageFileDto[]>([]);
  const [batchId, setBatchId] = useState('');
  const [parsed, setParsed] = useState<ParsedImportDto | null>(null);
  const [columnMap, setColumnMap] = useState<ImportColumnMap>(emptyMap);
  const [dateFormat, setDateFormat] = useState('M/d/yyyy');
  const [saveTemplate, setSaveTemplate] = useState(false);
  const [selectedRuleSetId, setSelectedRuleSetId] = useState('');
  const [previewRows, setPreviewRows] = useState<ImportPreviewRowDto[]>([]);
  const [commitResult, setCommitResult] = useState<ImportCommitResult | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [importRules, setImportRules] = useState<ImportRuleDto[]>([]);
  const [ruleSets, setRuleSets] = useState<ImportRuleSetDto[]>([]);
  const [newRuleSetName, setNewRuleSetName] = useState('');
  const [editingImportRuleId, setEditingImportRuleId] = useState<string | null>(null);
  const [classificationRules, setClassificationRules] = useState<ClassificationRuleDto[]>([]);
  const [ruleTest, setRuleTest] = useState('');
  const [ruleTestResult, setRuleTestResult] = useState<TestImportRuleResult | null>(null);
  const [classificationTest, setClassificationTest] = useState('');
  const [classificationTestResult, setClassificationTestResult] = useState<TestClassificationRuleResult | null>(null);
  const [importRuleForm, setImportRuleForm] = useState(emptyImportRuleForm);
  const [classificationRuleForm, setClassificationRuleForm] = useState({
    name: '',
    ruleType: 'keyword_contains',
    fieldTarget: 'description',
    value: '',
    classification: 'personal',
    alsoSetCategory: '',
    priority: 100,
    isActive: true,
  });

  useEffect(() => {
    reloadSupportingData();
  }, []);

  const columns = parsed?.columns ?? [];
  const acceptedCount = useMemo(() => previewRows.filter((row) => row.accepted).length, [previewRows]);
  const duplicateCount = useMemo(() => previewRows.filter((row) => row.isDuplicate).length, [previewRows]);
  const errorCount = useMemo(() => previewRows.filter((row) => row.errors.length > 0).length, [previewRows]);

  async function reloadSupportingData() {
    const [imports, sets, classifications, stored] = await Promise.all([
      listImportRules(),
      listImportRuleSets(),
      listClassificationRules(),
      listStorageFiles(),
    ]);
    setImportRules(imports);
    setRuleSets(sets);
    setClassificationRules(classifications);
    setStorageFiles(stored);
  }

  async function uploadAndParse(event: FormEvent) {
    event.preventDefault();
    if (!file) {
      setMessage('Choose a CSV file.');
      return;
    }

    await createBatchAndParse(() => uploadImport(accountId, institution, file), 'Unable to upload import.');
  }

  async function importStoredFile(event: FormEvent) {
    event.preventDefault();
    await createBatchAndParse(() => createImportFromStorage(accountId, institution, storedFileName), 'Unable to create import from stored file.');
  }

  async function createBatchAndParse(createBatch: () => Promise<{ id: string }>, errorMessage: string) {
    setBusy(true);
    setMessage(null);
    try {
      const uploaded = await createBatch();
      setBatchId(uploaded.id);
      const parsedResult = await parseImport(uploaded.id);
      setParsed(parsedResult);
      setColumnMap(autoMap(parsedResult.columns));
      setPreviewRows([]);
      setCommitResult(null);
    } catch (err) {
      setMessage(err instanceof Error ? err.message : errorMessage);
    } finally {
      setBusy(false);
    }
  }

  async function buildPreview(event: FormEvent) {
    event.preventDefault();
    if (!batchId) {
      return;
    }

    setBusy(true);
    setMessage(null);
    try {
      const request: PreviewImportRequest = {
        columnMap,
        ruleSetId: selectedRuleSetId || null,
        dateFormat,
        amountFormat: null,
        saveTemplate,
        templateName: saveTemplate ? `${institution || 'Bank'} CSV` : null,
      };
      setPreviewRows(await previewImport(batchId, request));
      setCommitResult(null);
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to build preview.');
    } finally {
      setBusy(false);
    }
  }

  async function updateRow(row: ImportPreviewRowDto, patch: Parameters<typeof updatePreviewRow>[2]) {
    if (!batchId) {
      return;
    }

    const updated = await updatePreviewRow(batchId, row.id, patch);
    setPreviewRows((rows) => rows.map((item) => (item.id === updated.id ? updated : item)));
  }

  async function commit() {
    if (!batchId) {
      return;
    }

    setBusy(true);
    setMessage(null);
    try {
      setCommitResult(await commitImport(batchId));
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to commit import.');
    } finally {
      setBusy(false);
    }
  }

  async function submitImportRule(event: FormEvent) {
    event.preventDefault();
    const request: UpsertImportRuleRequest = {
      ...importRuleForm,
      ruleSetId: importRuleForm.ruleSetId || null,
      pattern: importRuleForm.pattern || null,
      sourceField: importRuleForm.sourceField || null,
      targetField: importRuleForm.targetField || null,
      mapsToDescription: importRuleForm.mapsToDescription || null,
      mapsToCategory: importRuleForm.mapsToCategory || null,
      mapsToType: importRuleForm.mapsToType || null,
      mapsToClassification: importRuleForm.mapsToClassification || null,
    };
    if (editingImportRuleId) {
      await updateImportRule(editingImportRuleId, request);
    } else {
      await createImportRule(request);
    }
    setImportRuleForm(emptyImportRuleForm);
    setEditingImportRuleId(null);
    await reloadSupportingData();
  }

  async function submitRuleSet(event: FormEvent) {
    event.preventDefault();
    const created = await createImportRuleSet(newRuleSetName, institution || undefined);
    setNewRuleSetName('');
    setSelectedRuleSetId(created.id);
    await reloadSupportingData();
  }

  async function submitClassificationRule(event: FormEvent) {
    event.preventDefault();
    await createClassificationRule({
      ...classificationRuleForm,
      alsoSetCategory: classificationRuleForm.alsoSetCategory || null,
      classification: classificationRuleForm.classification as TransactionClassification,
    });
    setClassificationRuleForm({ ...classificationRuleForm, name: '', value: '', alsoSetCategory: '' });
    await reloadSupportingData();
  }

  const previewColumns: TableColumn<ImportPreviewRowDto>[] = [
    { key: 'accepted', label: 'Use', render: (row) => <Checkbox checked={row.accepted} onChange={(event) => updateRow(row, { accepted: event.target.checked })} /> },
    { key: 'rowNumber', label: '#', width: 60 },
    { key: 'date', label: 'Date', render: (row) => row.date ? formatDate(row.date) : 'Missing' },
    {
      key: 'cleanedDescription',
      label: 'Description',
      render: (row) => <Input aria-label="Cleaned description" defaultValue={row.cleanedDescription ?? ''} onBlur={(event) => updateRow(row, { cleanedDescription: event.target.value })} />
    },
    {
      key: 'type',
      label: 'Type',
      render: (row) => (
        <Select aria-label="Transaction type" value={row.type ?? ''} onChange={(event) => updateRow(row, { type: event.target.value })}>
          <option value="">Select</option>
          {transactionTypes.map((type) => <option key={type} value={type}>{displayEnum(type)}</option>)}
        </Select>
      )
    },
    {
      key: 'classification',
      label: 'Class',
      render: (row) => (
        <Select aria-label="Classification" value={row.classification} onChange={(event) => updateRow(row, { classification: event.target.value })}>
          {classifications.map((classification) => <option key={classification} value={classification}>{displayEnum(classification)}</option>)}
        </Select>
      )
    },
    { key: 'category', label: 'Category', render: (row) => <Input aria-label="Category" defaultValue={row.category ?? ''} onBlur={(event) => updateRow(row, { category: event.target.value })} /> },
    { key: 'amount', label: 'Amount', align: 'right', render: (row) => typeof row.amount === 'number' ? formatMoney(row.amount, 'USD') : 'Missing' },
    {
      key: 'status',
      label: 'Status',
      render: (row) => row.errors.length > 0
        ? <StatusBadge label="Error" tone="danger" />
        : row.isDuplicate
          ? <StatusBadge label="Duplicate" tone="warning" />
          : <StatusBadge label="Ready" tone="success" />
    },
  ];

  return (
    <section className="page">
      <header className="page-header">
        <div>
          <h1 className="page-title">Imports</h1>
          <p className="page-subtitle">Upload CSVs, reuse storage-room files, map columns, review duplicates, and commit accepted rows.</p>
        </div>
        {commitResult && <StatusBadge label={`${commitResult.imported} imported, ${commitResult.skippedDuplicates} duplicates, ${commitResult.rejected} rejected`} tone="success" />}
      </header>

      {message && <div className="notice error">{message}</div>}

      <form className="panel form-grid" onSubmit={uploadAndParse}>
        <Select label="Account" value={accountId} onChange={(event) => setAccountId(event.target.value)} required>
          <option value="">Select account</option>
          {accounts.map((account) => <option key={account.id} value={account.id}>{account.nickname}</option>)}
        </Select>
        <Input label="Institution" value={institution} onChange={(event) => setInstitution(event.target.value)} />
        <Input label="CSV file" type="file" accept=".csv,text/csv" onChange={(event) => setFile(event.target.files?.[0] ?? null)} />
        <Button type="submit" loading={busy} leftIcon={<Upload size={15} />}>Upload CSV</Button>
      </form>

      <form className="panel form-grid" onSubmit={importStoredFile}>
        <Select label="Stored file" value={storedFileName} onChange={(event) => setStoredFileName(event.target.value)} required>
          <option value="">Choose stored file</option>
          {storageFiles.map((item) => <option key={item.id} value={item.storedFileName}>{item.storedFileName}</option>)}
        </Select>
        <Button type="submit" loading={busy} leftIcon={<Play size={15} />}>Use Stored File</Button>
      </form>

      {parsed && (
        <form className="panel" onSubmit={buildPreview}>
          <div className="section-heading">
            <div>
              <h2>Column Mapping</h2>
              <p>{parsed.sampleRows.length} sample rows loaded from {parsed.columns.length} detected columns.</p>
            </div>
            <Button type="submit" loading={busy} leftIcon={<Play size={15} />}>Build Preview</Button>
          </div>
          <div className="form-grid">
            <ColumnSelect label="Date" value={columnMap.date} columns={columns} onChange={(value) => setColumnMap({ ...columnMap, date: value })} required />
            <ColumnSelect label="Description" value={columnMap.description} columns={columns} onChange={(value) => setColumnMap({ ...columnMap, description: value })} required />
            <ColumnSelect label="Merchant" value={columnMap.merchant} columns={columns} onChange={(value) => setColumnMap({ ...columnMap, merchant: value })} />
            <ColumnSelect label="Amount" value={columnMap.amount} columns={columns} onChange={(value) => setColumnMap({ ...columnMap, amount: value })} />
            <ColumnSelect label="Debit" value={columnMap.debit} columns={columns} onChange={(value) => setColumnMap({ ...columnMap, debit: value })} />
            <ColumnSelect label="Credit" value={columnMap.credit} columns={columns} onChange={(value) => setColumnMap({ ...columnMap, credit: value })} />
            <ColumnSelect label="Category" value={columnMap.category} columns={columns} onChange={(value) => setColumnMap({ ...columnMap, category: value })} />
            <ColumnSelect label="Classification" value={columnMap.classification} columns={columns} onChange={(value) => setColumnMap({ ...columnMap, classification: value })} />
            <Select label="Rule set" value={selectedRuleSetId} onChange={(event) => setSelectedRuleSetId(event.target.value)}>
              <option value="">No rule set</option>
              {ruleSets.map((set) => <option key={set.id} value={set.id}>{set.name}</option>)}
            </Select>
            <Input label="Date format" value={dateFormat} onChange={(event) => setDateFormat(event.target.value)} />
            <Checkbox label="Save mapping template" checked={saveTemplate} onChange={(event) => setSaveTemplate(event.target.checked)} />
          </div>
        </form>
      )}

      <div className="panel">
        <div className="section-heading">
          <div>
            <h2>Preview</h2>
            <p>{acceptedCount} accepted, {duplicateCount} duplicates, {errorCount} errors.</p>
          </div>
          <Button type="button" disabled={previewRows.length === 0 || busy} loading={busy} leftIcon={<Check size={15} />} onClick={commit}>Commit</Button>
        </div>
        {previewRows.length === 0 ? <EmptyState title="No preview rows" message="Upload or select a stored CSV and build a preview." /> : <Table data={previewRows} columns={previewColumns} rowKey="id" />}
      </div>

      <div className="panel split-panel">
        <RulesPanel
          rows={importRules}
          ruleSets={ruleSets}
          newRuleSetName={newRuleSetName}
          onNewRuleSetNameChange={setNewRuleSetName}
          onCreateRuleSet={submitRuleSet}
          editingRuleId={editingImportRuleId}
          form={importRuleForm}
          onFormChange={setImportRuleForm}
          onSubmit={submitImportRule}
          onCancelEdit={() => {
            setEditingImportRuleId(null);
            setImportRuleForm(emptyImportRuleForm);
          }}
          onEdit={(rule) => {
            setEditingImportRuleId(rule.id);
            setImportRuleForm({
              ruleSetId: rule.ruleSetId ?? '',
              name: rule.name,
              pattern: rule.pattern ?? '',
              sourceField: rule.sourceField ?? '',
              targetField: rule.targetField ?? '',
              valueTransform: rule.valueTransform,
              mapsToDescription: rule.mapsToDescription ?? '',
              mapsToCategory: rule.mapsToCategory ?? '',
              mapsToType: rule.mapsToType ?? '',
              mapsToClassification: rule.mapsToClassification ?? '',
              priority: rule.priority,
              isActive: rule.isActive,
            });
          }}
          onDelete={async (id) => {
            await deleteImportRule(id);
            await reloadSupportingData();
          }}
          testValue={ruleTest}
          onTestValueChange={setRuleTest}
          onTest={async () => setRuleTestResult(await testImportRule(ruleTest))}
          testResult={ruleTestResult ? `${ruleTestResult.cleanedDescription} · ${displayEnum(ruleTestResult.classification)}` : ''}
        />
      </div>

      <div className="panel split-panel">
        <ClassificationRulesPanel
          rows={classificationRules}
          form={classificationRuleForm}
          onFormChange={setClassificationRuleForm}
          onSubmit={submitClassificationRule}
          testValue={classificationTest}
          onTestValueChange={setClassificationTest}
          onTest={async () => setClassificationTestResult(await testClassificationRule(classificationTest, 0))}
          testResult={classificationTestResult ? `${displayEnum(classificationTestResult.classification)}${classificationTestResult.category ? ` · ${classificationTestResult.category}` : ''}` : ''}
        />
      </div>
    </section>
  );
}

function ColumnSelect({ label, value, columns, onChange, required = false }: { label: string; value?: string | null; columns: string[]; onChange: (value: string) => void; required?: boolean }) {
  return (
    <Select label={label} value={value ?? ''} onChange={(event) => onChange(event.target.value)} required={required}>
      <option value="">Not mapped</option>
      {columns.map((column) => <option key={column} value={column}>{column}</option>)}
    </Select>
  );
}

function RulesPanel({
  rows,
  ruleSets,
  newRuleSetName,
  onNewRuleSetNameChange,
  onCreateRuleSet,
  editingRuleId,
  form,
  onFormChange,
  onSubmit,
  onCancelEdit,
  onEdit,
  onDelete,
  testValue,
  onTestValueChange,
  onTest,
  testResult,
}: {
  rows: ImportRuleDto[];
  ruleSets: ImportRuleSetDto[];
  newRuleSetName: string;
  onNewRuleSetNameChange: (value: string) => void;
  onCreateRuleSet: (event: FormEvent) => void;
  editingRuleId: string | null;
  form: typeof emptyImportRuleForm;
  onFormChange: (value: typeof emptyImportRuleForm) => void;
  onSubmit: (event: FormEvent) => void;
  onCancelEdit: () => void;
  onEdit: (rule: ImportRuleDto) => void;
  onDelete: (id: string) => Promise<void>;
  testValue: string;
  onTestValueChange: (value: string) => void;
  onTest: () => Promise<void>;
  testResult: string;
}) {
  return (
    <div>
      <div className="section-heading">
        <div>
          <h2>Import Rules</h2>
          <p>Use simple field maps for renamed columns and optional regex cleanup for descriptions.</p>
        </div>
      </div>

      <form className="inline-actions" onSubmit={onCreateRuleSet}>
        <Input label="New rule set" value={newRuleSetName} onChange={(event) => onNewRuleSetNameChange(event.target.value)} />
        <Button type="submit">Add Set</Button>
      </form>

      <form className="compact-form" onSubmit={onSubmit}>
        <Select label="Rule set" value={form.ruleSetId} onChange={(event) => onFormChange({ ...form, ruleSetId: event.target.value })}>
          <option value="">No set</option>
          {ruleSets.map((set) => <option key={set.id} value={set.id}>{set.name}</option>)}
        </Select>
        <Input label="Name" value={form.name} onChange={(event) => onFormChange({ ...form, name: event.target.value })} required />
        <Input label="Source field" value={form.sourceField} onChange={(event) => onFormChange({ ...form, sourceField: event.target.value })} />
        <Select label="Target field" value={form.targetField} onChange={(event) => onFormChange({ ...form, targetField: event.target.value })}>
          <option value="">None</option>
          {targetFields.map((field) => <option key={field} value={field}>{displayEnum(field)}</option>)}
        </Select>
        <Select label="Transform" value={form.valueTransform} onChange={(event) => onFormChange({ ...form, valueTransform: event.target.value })}>
          {valueTransforms.map((transform) => <option key={transform} value={transform}>{displayEnum(transform)}</option>)}
        </Select>
        <Input label="Regex pattern" value={form.pattern} onChange={(event) => onFormChange({ ...form, pattern: event.target.value })} />
        <Input label="Clean description" value={form.mapsToDescription} onChange={(event) => onFormChange({ ...form, mapsToDescription: event.target.value })} />
        <Input label="Category" value={form.mapsToCategory} onChange={(event) => onFormChange({ ...form, mapsToCategory: event.target.value })} />
        <Select label="Type" value={form.mapsToType} onChange={(event) => onFormChange({ ...form, mapsToType: event.target.value })}>
          <option value="">No change</option>
          {transactionTypes.map((type) => <option key={type} value={type}>{displayEnum(type)}</option>)}
        </Select>
        <Select label="Classification" value={form.mapsToClassification} onChange={(event) => onFormChange({ ...form, mapsToClassification: event.target.value })}>
          <option value="">No change</option>
          {classifications.map((classification) => <option key={classification} value={classification}>{displayEnum(classification)}</option>)}
        </Select>
        <Button type="submit">{editingRuleId ? 'Save Rule' : 'Add Rule'}</Button>
        {editingRuleId && <Button type="button" variant="secondary" onClick={onCancelEdit}>Cancel</Button>}
      </form>

      <div className="inline-actions">
        <Input label="Test description" value={testValue} onChange={(event) => onTestValueChange(event.target.value)} />
        <Button type="button" variant="secondary" leftIcon={<FlaskConical size={15} />} onClick={onTest}>Test</Button>
      </div>
      {testResult && <p className="muted">{testResult}</p>}
      <Table
        data={rows}
        rowKey="id"
        emptyMessage="No import rules."
        columns={[
          { key: 'priority', label: 'Priority' },
          { key: 'name', label: 'Name' },
          { key: 'sourceField', label: 'Source' },
          { key: 'targetField', label: 'Target', render: (rule) => displayEnum(rule.targetField) },
          { key: 'pattern', label: 'Pattern' },
          {
            key: 'actions',
            label: '',
            align: 'right',
            render: (rule) => (
              <div className="row-actions">
                <Button type="button" variant="secondary" size="sm" leftIcon={<Pencil size={14} />} onClick={() => onEdit(rule)}>Edit</Button>
                <Button type="button" variant="destructive" size="sm" leftIcon={<Trash2 size={14} />} onClick={() => onDelete(rule.id)}>Delete</Button>
              </div>
            )
          },
        ]}
      />
    </div>
  );
}

function ClassificationRulesPanel({
  rows,
  form,
  onFormChange,
  onSubmit,
  testValue,
  onTestValueChange,
  onTest,
  testResult,
}: {
  rows: ClassificationRuleDto[];
  form: { name: string; ruleType: string; fieldTarget: string; value: string; classification: string; alsoSetCategory: string; priority: number; isActive: boolean };
  onFormChange: (value: typeof form) => void;
  onSubmit: (event: FormEvent) => void;
  testValue: string;
  onTestValueChange: (value: string) => void;
  onTest: () => Promise<void>;
  testResult: string;
}) {
  return (
    <div>
      <div className="section-heading">
        <div>
          <h2>Classification Rules</h2>
          <p>First active matching rule wins on import and manual transaction save.</p>
        </div>
      </div>
      <form className="compact-form" onSubmit={onSubmit}>
        <Input label="Name" value={form.name} onChange={(event) => onFormChange({ ...form, name: event.target.value })} required />
        <Select label="Rule type" value={form.ruleType} onChange={(event) => onFormChange({ ...form, ruleType: event.target.value })}>
          {['keyword_contains', 'merchant_exact', 'category_is', 'amount_gte', 'amount_lte', 'amount_range'].map((value) => <option key={value} value={value}>{displayEnum(value)}</option>)}
        </Select>
        <Select label="Field" value={form.fieldTarget} onChange={(event) => onFormChange({ ...form, fieldTarget: event.target.value })}>
          {['description', 'merchant', 'category', 'amount'].map((value) => <option key={value} value={value}>{displayEnum(value)}</option>)}
        </Select>
        <Input label="Value" value={form.value} onChange={(event) => onFormChange({ ...form, value: event.target.value })} required />
        <Select label="Classification" value={form.classification} onChange={(event) => onFormChange({ ...form, classification: event.target.value })}>
          {classifications.map((classification) => <option key={classification} value={classification}>{displayEnum(classification)}</option>)}
        </Select>
        <Input label="Set category" value={form.alsoSetCategory} onChange={(event) => onFormChange({ ...form, alsoSetCategory: event.target.value })} />
        <Button type="submit">Add Rule</Button>
      </form>
      <div className="inline-actions">
        <Input label="Test description" value={testValue} onChange={(event) => onTestValueChange(event.target.value)} />
        <Button type="button" variant="secondary" leftIcon={<FlaskConical size={15} />} onClick={onTest}>Test</Button>
      </div>
      {testResult && <p className="muted">{testResult}</p>}
      <Table
        data={rows}
        rowKey="id"
        emptyMessage="No classification rules."
        columns={[
          { key: 'priority', label: 'Priority' },
          { key: 'name', label: 'Name' },
          { key: 'ruleType', label: 'Rule', render: (rule) => displayEnum(rule.ruleType) },
          { key: 'classification', label: 'Class', render: (rule) => displayEnum(rule.classification) },
        ]}
      />
    </div>
  );
}

function autoMap(columns: string[]): ImportColumnMap {
  const find = (...terms: string[]) => columns.find((column) => terms.some((term) => column.toLowerCase().includes(term))) ?? '';
  return {
    date: find('date', 'posted'),
    description: find('description', 'memo', 'name', 'payee'),
    merchant: find('merchant'),
    amount: find('amount'),
    debit: find('debit', 'withdrawal'),
    credit: find('credit', 'deposit'),
    type: find('type'),
    category: find('category'),
    classification: find('classification'),
  };
}
