import type { InputHTMLAttributes } from 'react';

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  error?: string;
  helperText?: string;
}

export function Input({ label, error, helperText, className = '', id, ...props }: InputProps) {
  const inputId = id ?? (label ? label.toLowerCase().replace(/\s+/g, '-') : undefined);

  return (
    <label className="pm-field" htmlFor={inputId}>
      {label && <span className="pm-label">{label}</span>}
      <input id={inputId} className={`pm-input ${className}`} {...props} />
      {error && <span className="pm-field-error">{error}</span>}
      {helperText && !error && <span className="pm-help">{helperText}</span>}
    </label>
  );
}
