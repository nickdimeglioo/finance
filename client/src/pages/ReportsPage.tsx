import { EmptyState } from '../components/ui';

export function ReportsPage() {
  return (
    <section className="page">
      <header className="page-header">
        <div>
          <h1 className="page-title">Reports</h1>
          <p className="page-subtitle">Charts, exports, and reconciliation reporting are scheduled for Phase 6.</p>
        </div>
      </header>
      <div className="panel">
        <EmptyState title="Reports workspace" message="Server-side aggregations, reconciliation views, charts, and exports will drive this page." />
      </div>
    </section>
  );
}
