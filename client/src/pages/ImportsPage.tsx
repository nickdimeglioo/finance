import { FormEvent, useEffect, useMemo, useState } from 'react';
import { ExternalLink, Plus } from 'lucide-react';
import { Link, useNavigate } from 'react-router-dom';
import { Button, EmptyState, Input, StatusBadge, Table, type TableColumn } from '../components/ui';
import { createRuleset, listRulesets } from '../services/importsService';
import type { RulesetDto, RulesetRuleDefinitionDto, RulesetRulesDocumentDto } from '../types/schema';

export function ImportsPage() {
  const navigate = useNavigate();
  const [rulesets, setRulesets] = useState<RulesetDto[]>([]);
  const [newRulesetName, setNewRulesetName] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const activeRulesets = useMemo(() => rulesets.filter((ruleset) => ruleset.isActive), [rulesets]);

  useEffect(() => {
    listRulesets()
      .then(setRulesets)
      .catch((err: Error) => setMessage(err.message));
  }, []);

  async function submitRuleset(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setMessage(null);
    try {
      const created = await createRuleset({
        name: newRulesetName,
        description: null,
        sourceConfig: { delimiter: ',', encoding: 'utf-8', expectedColumns: [] },
        rules: createStarterRulesDocument(),
        isActive: true,
      });
      navigate(`/imports/rulesets/${created.id}`);
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to create ruleset.');
    } finally {
      setBusy(false);
    }
  }

  const columns: TableColumn<RulesetDto>[] = [
    {
      key: 'name',
      label: 'Ruleset',
      render: (ruleset) => <Link className="table-resource-link" to={`/imports/rulesets/${ruleset.id}`}>{ruleset.name}<ExternalLink size={14} /></Link>,
    },
    { key: 'description', label: 'Description', render: (ruleset) => ruleset.description ?? 'Statement import rules' },
    { key: 'rules', label: 'Rules', align: 'right', render: (ruleset) => ruleset.rules.rules?.length ?? 0 },
    { key: 'version', label: 'Version', align: 'right' },
    { key: 'isActive', label: 'Status', render: () => <StatusBadge label="Active" tone="success" /> },
    { key: 'updatedAt', label: 'Updated', render: (ruleset) => ruleset.updatedAt.slice(0, 10) },
  ];

  return (
    <section className="page">
      <header className="page-header">
        <div>
          <h1 className="page-title">Imports</h1>
          <p className="page-subtitle">Create a ruleset, then open it to manage rules and import statements.</p>
        </div>
      </header>

      {message && <div className="notice error">{message}</div>}

      <div className="panel">
        <div className="section-heading">
          <div>
            <h2>Rulesets</h2>
            <p>{activeRulesets.length} active rulesets.</p>
          </div>
        </div>
        <form className="inline-actions" onSubmit={submitRuleset}>
          <Input label="New ruleset name" value={newRulesetName} onChange={(event) => setNewRulesetName(event.target.value)} required />
          <Button type="submit" loading={busy} leftIcon={<Plus size={15} />}>Create Ruleset</Button>
        </form>
        {activeRulesets.length === 0
          ? <EmptyState title="No rulesets" message="Create a ruleset to begin importing statements." />
          : <Table data={activeRulesets} columns={columns} rowKey="id" />}
      </div>
    </section>
  );
}

function createStarterRulesDocument(): RulesetRulesDocumentDto {
  return {
    onError: 'skip',
    fallback: { category: 'Uncategorized', classification: 'unknown', tags: [] },
    rules: [
      fieldRule('date', 'Date', 10, 'Date', 'parseDate', 'M/d/yyyy'),
      fieldRule('description', 'Description', 20, 'Description', 'trim'),
      {
        id: 'amount-debit-credit',
        name: 'Amount from Debit/Credit',
        kind: 'field',
        priority: 30,
        isActive: true,
        target: 'amount',
        flow: [
          { when: { op: 'isNotEmpty', field: 'row["Debit"]' }, source: 'Debit', transform: { type: 'toDecimal' } },
          { when: { op: 'isNotEmpty', field: 'row["Credit"]' }, source: 'Credit', transform: { type: 'toDecimal' } },
          { when: { op: 'isNotEmpty', field: 'row["Amount"]' }, source: 'Amount', transform: { type: 'toDecimal' } },
        ],
      },
      { id: 'is-debit', name: 'Debit flag', kind: 'field', priority: 40, isActive: true, target: 'isDebit', flow: [{ when: { op: 'isNotEmpty', field: 'row["Debit"]' }, value: true }] },
      { id: 'is-credit', name: 'Credit flag', kind: 'field', priority: 41, isActive: true, target: 'isCredit', flow: [{ when: { op: 'isNotEmpty', field: 'row["Credit"]' }, value: true }] },
      { id: 'type-debit', name: 'Debit is expense', kind: 'field', priority: 50, isActive: true, target: 'type', match: { op: 'equals', field: 'isDebit', value: true }, flow: [{ value: 'expense', transform: { type: 'toEnum' } }] },
      { id: 'type-credit', name: 'Credit is income', kind: 'field', priority: 51, isActive: true, target: 'type', match: { op: 'equals', field: 'isCredit', value: true }, flow: [{ value: 'income', transform: { type: 'toEnum' } }] },
      { id: 'unique-id', name: 'Unique row id', kind: 'field', priority: 60, isActive: true, target: 'uniqueId', flow: [{ expr: 'CONCAT(Date, "|", Description, "|", Debit, "|", Credit)', transform: { type: 'trim' } }] },
    ],
  };
}

function fieldRule(target: string, name: string, priority: number, source: string, transformType?: string, format?: string): RulesetRuleDefinitionDto {
  return {
    id: `field-${target}`,
    name,
    kind: 'field',
    priority,
    isActive: true,
    target,
    flow: [{ source, transform: transformType ? { type: transformType, format } : null }],
  };
}
