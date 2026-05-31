import { EmptyState } from '../components/ui';

export function SubscriptionsPage() {
  return (
    <section className="page">
      <header className="page-header">
        <div>
          <h1 className="page-title">Subscriptions</h1>
          <p className="page-subtitle">Recurring rules and subscription tracking are scheduled for Phase 5.</p>
        </div>
      </header>
      <div className="panel">
        <EmptyState title="Subscriptions workspace" message="Recurring spend, renewal dates, and missing transaction alerts will appear here." />
      </div>
    </section>
  );
}
