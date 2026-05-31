interface EmptyStateProps {
  title: string;
  message?: string;
}

export function EmptyState({ title, message }: EmptyStateProps) {
  return (
    <div className="pm-empty">
      <strong>{title}</strong>
      {message && <span>{message}</span>}
    </div>
  );
}
