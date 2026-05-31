import { EmptyState } from '../components/ui';

export function ImportsPage() {
  return (
    <section className="page">
      <header className="page-header">
        <div>
          <h1 className="page-title">Imports</h1>
          <p className="page-subtitle">Upload, mapping, preview, rules, and commit workflows are scheduled for Phase 4.</p>
        </div>
      </header>
      <div className="panel">
        <EmptyState title="Import batches" message="S3 object keys, mapping, preview, rules, and commit workflows will be built here." />
      </div>
    </section>
  );
}
