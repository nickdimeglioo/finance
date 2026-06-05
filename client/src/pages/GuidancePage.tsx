import { useEffect, useState } from 'react';
import { StatusBadge } from '../components/ui';
import { formatDate, formatMoney } from '../lib/financeFormat';
import { getGuidance } from '../services/guidanceService';
import type { GuidanceItemDto, GuidanceSummaryDto } from '../types/schema';

const tones: Record<GuidanceItemDto['status'], 'success' | 'warning' | 'danger' | 'neutral'> = {
  on_track: 'success',
  warning: 'warning',
  below_target: 'danger',
  no_data: 'neutral',
};

export function GuidancePage() {
  const [summary, setSummary] = useState<GuidanceSummaryDto | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    getGuidance().then(setSummary).catch((err: Error) => setMessage(err.message));
  }, []);

  return (
    <section className="page">
      <header className="page-header">
        <div>
          <h1 className="page-title">Guidance</h1>
          <p className="page-subtitle">
            Deterministic guidance from {summary ? `${formatDate(summary.from)} to ${formatDate(summary.to)}` : 'the current 3-month window'}.
          </p>
        </div>
      </header>

      {message && <div className="notice error">{message}</div>}
      {!summary && !message && <div className="panel">Loading guidance...</div>}
      {summary && (
        <div className="guidance-grid">
          {summary.guidance.map((item) => (
            <article key={item.id} className="guidance-card">
              <div className="panel-header">
                <h2>{item.title}</h2>
                <StatusBadge label={item.status.replaceAll('_', ' ')} tone={tones[item.status]} />
              </div>
              <p>{item.message}</p>
              <dl className="metric-list">
                {Object.entries(item.supportingMetrics).map(([key, value]) => (
                  <div key={key}>
                    <dt>{key.replace(/([A-Z])/g, ' $1')}</dt>
                    <dd>{key.toLowerCase().includes('percent') || key.toLowerCase().includes('target') || key.toLowerCase().includes('threshold') ? `${value.toFixed(2)}%` : formatMoney(value)}</dd>
                  </div>
                ))}
              </dl>
            </article>
          ))}
        </div>
      )}
    </section>
  );
}
