type StatusTone = 'neutral' | 'success' | 'warning' | 'danger' | 'info';

interface StatusBadgeProps {
  label: string;
  tone?: StatusTone;
}

export function StatusBadge({ label, tone = 'neutral' }: StatusBadgeProps) {
  return (
    <span className={`pm-status-pill tone-${tone}`}>
      <span className="pm-status-dot" aria-hidden="true" />
      {label}
    </span>
  );
}
