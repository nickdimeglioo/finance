import type { SelectHTMLAttributes } from 'react';

interface SelectProps extends SelectHTMLAttributes<HTMLSelectElement> {
  label?: string;
  error?: string;
  helperText?: string;
}

export function Select({ label, error, helperText, className = '', id, children, ...props }: SelectProps) {
  const inputId = id ?? (label ? label.toLowerCase().replace(/\s+/g, '-') : undefined);

  return (
    <label className="pm-field" htmlFor={inputId}>
      {label && <span className="pm-label">{label}</span>}
      <select id={inputId} className={`pm-input pm-select ${className}`} {...props}>
        {children}
      </select>
      {error && <span className="pm-field-error">{error}</span>}
      {helperText && !error && <span className="pm-help">{helperText}</span>}
    </label>
  );
}
