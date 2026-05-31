import { FormEvent, useEffect, useMemo, useState } from 'react';
import { Check, FlaskConical, Play, Upload } from 'lucide-react';
import { Button, Checkbox, EmptyState, Input, Select, StatusBadge, Table, type TableColumn } from '../components/ui';
import { formatDate, formatMoney } from '../lib/financeFormat';
import { useAccounts } from '../services/accountsService';
import {
  commitImport,
  createClassificationRule,
  createImportRule,
  listClassificationRules,
  listImportRules,
  parseImport,
  previewImport,
  testClassificationRule,
  testImportRule,
  updatePreviewRow,
  uploadImport,
} from '../services/importsService';
import type {
  ClassificationRuleDto,
  ImportColumnMap,
  ImportCommitResult,
  ImportPreviewRowDto,
  ImportRuleDto,
  ParsedImportDto,
  PreviewImportRequest,
  TestClassificationRuleResult,
  TestImportRuleResult,
  TransactionClassification,
  TransactionType,
} from '../types/schema';

const classifications: TransactionClassification[] = ['personal', 'business', 'mixed', 'ignored', 'unknown'];
const transactionTypes: TransactionType[] = ['expense', 'income', 'transfer', 'adjustment', 'opening_balance'];
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

export function ImportsPage() {
  const { accounts } = useAccounts();
  const [accountId, setAccountId] = useState('');
  const [institution, setInstitution] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [batchId, setBatchId] = useState('');
  const [parsed, setParsed] = useState<ParsedImportDto | null>(null);
  const [columnMap, setColumnMap] = useState<ImportColumnMap>(emptyMap);
  const [dateFormat, setDateFormat] = useState('M/d/yyyy');
  const [saveTemplate, setSaveTemplate] = useState(false);
  const [previewRows, setPreviewRows] = useState<ImportPreviewRowDto[]>([]);
  const [commitResult, setCommitResult] = useState<ImportCommitResult | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [importRules, setImportRules] = useState<ImportRuleDto[]>([]);
  const [classificationRules, setClassificationRules] = useState<ClassificationRuleDto[]>([]);
  const [ruleTest, setRuleTest] = useState('');
  const [ruleTestResult, setRuleTestResult] = useState<TestImportRuleResult | null>(null);
  const [classificationTest, setClassificationTest] = useState('');
  const [classificationTestResult, setClassificationTestResult] = useState<TestClassificationRuleResult | null>(null);
  const [importRuleForm, setImportRuleForm] = useState({
    name: '',
    pattern: '',
    mapsToDescription: '',
    mapsToCategory: '',
    mapsToType: 'expense',
    mapsToClassification: 'personal',
    priority: 100,
    isActive: true,
  });
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
    reloadRules();
  }, []);

  const columns = parsed?.columns ?? [];
  const acceptedCount = useMemo(() => previewRows.filter((row) => row.accepted).length, [previewRows]);
  const duplicateCount = useMemo(() => previewRows.filter((row) => row.isDuplicate).length, [previewRows]);
  const errorCount = useMemo(() => previewRows.filter((row) => row.errors.length > 0).length, [previewRows]);

  async function reloadRules() {
    const [imports, classifications] = await Promise.all([listImportRules(), listClassificationRules()]);
    setImportRules(imports);
    setClassificationRules(classifications);
  }

  async function submitUpload(event: FormEvent) {
    event.preventDefault();
    if (!file) {
      setMessage('Choose a CSV file.');
      return;
    }

    setBusy(true);
    setMessage(null);
    try {
      const uploaded = await uploadImport(accountId, institution, file);
      setBatchId(uploaded.id);
      const parsedResult = await parseImport(uploaded.id);
      setParsed(parsedResult);
      setColumnMap(autoMap(parsedResult.columns));
      setPreviewRows([]);
      setCommitResult(null);
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to upload import.');
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

  async function toggleRow(row: ImportPreviewRowDto, accepted: boolean) {
    await updateRow(row, { accepted });
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
    await createImportRule({
      ...importRuleForm,
      mapsToDescription: importRuleForm.mapsToDescription || null,
      mapsToCategory: importRuleForm.mapsToCategory || null,
      mapsToType: importRuleForm.mapsToType || null,
      mapsToClassification: importRuleForm.mapsToClassification || null,
    });
    setImportRuleForm({ ...importRuleForm, name: '', pattern: '', mapsToDescription: '', mapsToCategory: '' });
    await reloadRules();
  }

  async function submitClassificationRule(event: FormEvent) {
    event.preventDefault();
    await createClassificationRule({
      ...classificationRuleForm,
      alsoSetCategory: classificationRuleForm.alsoSetCategory || null,
      classification: classificationRuleForm.classification as TransactionClassification,
    });
    setClassificationRuleForm({ ...classificationRuleForm, name: '', value: '', alsoSetCategory: '' });
    await reloadRules();
  }

  const previewColumns: TableColumn<ImportPreviewRowDto>[] = [
    { key: 'accepted', label: 'Use', render: (row) => <Checkbox checked={row.accepted} onChange={(event) => toggleRow(row, event.target.checked)} /> },
    { key: 'rowNumber', label: '#', width: 60 },
    { key: 'date', label: 'Date', render: (row) => row.date ? formatDate(row.date) : 'Missing' },
    {
      key: 'cleanedDescription',
      label: 'Description',
      render: (row) => (
        <Input
          aria-label="Cleaned description"
          defaultValue={row.cleanedDescription ?? ''}
          onBlur={(event) => updateRow(row, { cleanedDescription: event.target.value })}
        />
      )
    },
    {
      key: 'type',
      label: 'Type',
      render: (row) => (
        <Select aria-label="Transaction type" value={row.type ?? ''} onChange={(event) => updateRow(row, { type: event.target.value })}>
          <option value="">Select</option>
          {transactionTypes.map((type) => <option key={type} value={type}>{type}</option>)}
        </Select>
      )
    },
    {
      key: 'classification',
      label: 'Class',
      render: (row) => (
        <Select aria-label="Classification" value={row.classification} onChange={(event) => updateRow(row, { classification: event.target.value })}>
          {classifications.map((classification) => <option key={classification} value={classification}>{classification}</option>)}
        </Select>
      )
    },
    {
      key: 'category',
      label: 'Category',
      render: (row) => (
        <Input
          aria-label="Category"
          defaultValue={row.category ?? ''}
          onBlur={(event) => updateRow(row, { category: event.target.value })}
        />
      )
    },
    { key: 'amount', label: 'Amount', align: 'right', render: (row) => typeof row.amount === 'number' ? formatMoney(row.amount, 'USD') : 'Missing' },
    {
      key: 'status',
      label: 'Status',
      render: (row) => row.errors.length > 0
        ? <StatusBadge label="error" tone="danger" />
        : row.isDuplicate
          ? <StatusBadge label="duplicate" tone="warning" />
          : <StatusBadge label="ready" tone="success" />
    },
  ];

  return (
    <section className="page">
      <header className="page-header">
        <div>
          <h1 className="page-title">Imports</h1>
          <p className="page-subtitle">Upload bank CSVs, map columns, review duplicate detection, and commit accepted rows.</p>
        </div>
        {commitResult && (
          <StatusBadge
            label={`${commitResult.imported} imported, ${commitResult.skippedDuplicates} duplicates, ${commitResult.rejected} rejected`}
            tone="success"
          />
        )}
      </header>

      {message && <div className="notice error">{message}</div>}

      <form className="panel form-grid" onSubmit={submitUpload}>
        <Select label="Account" value={accountId} onChange={(event) => setAccountId(event.target.value)} required>
          <option value="">Select account</option>
          {accounts.map((account) => <option key={account.id} value={account.id}>{account.nickname}</option>)}
        </Select>
        <Input label="Institution" value={institution} onChange={(event) => setInstitution(event.target.value)} />
        <Input label="CSV file" type="file" accept=".csv,text/csv" onChange={(event) => setFile(event.target.files?.[0] ?? null)} required />
        <Button type="submit" loading={busy} leftIcon={<Upload size={15} />}>Upload</Button>
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
        {previewRows.length === 0 ? (
          <EmptyState title="No preview rows" message="Upload a CSV and build a preview before committing transactions." />
        ) : (
          <Table data={previewRows} columns={previewColumns} rowKey="id" />
        )}
      </div>

      <div className="panel split-panel">
        <RulesPanel
          title="Import Rules"
          rows={importRules}
          form={importRuleForm}
          onFormChange={setImportRuleForm}
          onSubmit={submitImportRule}
          testValue={ruleTest}
          onTestValueChange={setRuleTest}
          onTest={async () => setRuleTestResult(await testImportRule(ruleTest))}
          testResult={ruleTestResult ? `${ruleTestResult.cleanedDescription} · ${ruleTestResult.classification}` : ''}
        />

        <ClassificationRulesPanel
          rows={classificationRules}
          form={classificationRuleForm}
          onFormChange={setClassificationRuleForm}
          onSubmit={submitClassificationRule}
          testValue={classificationTest}
          onTestValueChange={setClassificationTest}
          onTest={async () => setClassificationTestResult(await testClassificationRule(classificationTest, 0))}
          testResult={classificationTestResult ? `${classificationTestResult.classification}${classificationTestResult.category ? ` · ${classificationTestResult.category}` : ''}` : ''}
        />
      </div>
    </section>
  );
}

function ColumnSelect({
  label,
  value,
  columns,
  onChange,
  required = false,
}: {
  label: string;
  value?: string | null;
  columns: string[];
  onChange: (value: string) => void;
  required?: boolean;
}) {
  return (
    <Select label={label} value={value ?? ''} onChange={(event) => onChange(event.target.value)} required={required}>
      <option value="">Not mapped</option>
      {columns.map((column) => <option key={column} value={column}>{column}</option>)}
    </Select>
  );
}

function RulesPanel({
  title,
  rows,
  form,
  onFormChange,
  onSubmit,
  testValue,
  onTestValueChange,
  onTest,
  testResult,
}: {
  title: string;
  rows: ImportRuleDto[];
  form: {
    name: string;
    pattern: string;
    mapsToDescription: string;
    mapsToCategory: string;
    mapsToType: string;
    mapsToClassification: string;
    priority: number;
    isActive: boolean;
  };
  onFormChange: (value: typeof form) => void;
  onSubmit: (event: FormEvent) => void;
  testValue: string;
  onTestValueChange: (value: string) => void;
  onTest: () => Promise<void>;
  testResult: string;
}) {
  return (
    <div>
      <h2>{title}</h2>
      <form className="compact-form" onSubmit={onSubmit}>
        <Input label="Name" value={form.name} onChange={(event) => onFormChange({ ...form, name: event.target.value })} required />
        <Input label="Pattern" value={form.pattern} onChange={(event) => onFormChange({ ...form, pattern: event.target.value })} required />
        <Input label="Clean description" value={form.mapsToDescription} onChange={(event) => onFormChange({ ...form, mapsToDescription: event.target.value })} />
        <Input label="Category" value={form.mapsToCategory} onChange={(event) => onFormChange({ ...form, mapsToCategory: event.target.value })} />
        <Select label="Type" value={form.mapsToType} onChange={(event) => onFormChange({ ...form, mapsToType: event.target.value })}>
          {transactionTypes.map((type) => <option key={type} value={type}>{type}</option>)}
        </Select>
        <Select label="Classification" value={form.mapsToClassification} onChange={(event) => onFormChange({ ...form, mapsToClassification: event.target.value })}>
          {classifications.map((classification) => <option key={classification} value={classification}>{classification}</option>)}
        </Select>
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
        emptyMessage="No import rules."
        columns={[
          { key: 'priority', label: 'Priority' },
          { key: 'name', label: 'Name' },
          { key: 'pattern', label: 'Pattern' },
          { key: 'mapsToClassification', label: 'Class' },
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
  form: {
    name: string;
    ruleType: string;
    fieldTarget: string;
    value: string;
    classification: string;
    alsoSetCategory: string;
    priority: number;
    isActive: boolean;
  };
  onFormChange: (value: typeof form) => void;
  onSubmit: (event: FormEvent) => void;
  testValue: string;
  onTestValueChange: (value: string) => void;
  onTest: () => Promise<void>;
  testResult: string;
}) {
  return (
    <div>
      <h2>Classification Rules</h2>
      <form className="compact-form" onSubmit={onSubmit}>
        <Input label="Name" value={form.name} onChange={(event) => onFormChange({ ...form, name: event.target.value })} required />
        <Select label="Rule type" value={form.ruleType} onChange={(event) => onFormChange({ ...form, ruleType: event.target.value })}>
          {['keyword_contains', 'merchant_exact', 'category_is', 'amount_gte', 'amount_lte', 'amount_range'].map((value) => <option key={value} value={value}>{value}</option>)}
        </Select>
        <Select label="Field" value={form.fieldTarget} onChange={(event) => onFormChange({ ...form, fieldTarget: event.target.value })}>
          {['description', 'merchant', 'category', 'amount'].map((value) => <option key={value} value={value}>{value}</option>)}
        </Select>
        <Input label="Value" value={form.value} onChange={(event) => onFormChange({ ...form, value: event.target.value })} required />
        <Select label="Classification" value={form.classification} onChange={(event) => onFormChange({ ...form, classification: event.target.value })}>
          {classifications.map((classification) => <option key={classification} value={classification}>{classification}</option>)}
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
          { key: 'ruleType', label: 'Rule' },
          { key: 'classification', label: 'Class' },
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
