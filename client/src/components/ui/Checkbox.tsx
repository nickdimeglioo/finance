import type { InputHTMLAttributes, ReactNode } from 'react';

interface CheckboxProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> {
  label?: ReactNode;
  helperText?: string;
}

export function Checkbox({ label, helperText, className = '', ...props }: CheckboxProps) {
  return (
    <label className={`pm-checkbox ${className}`}>
      <input type="checkbox" {...props} />
      <span>
        {label}
        {helperText && <small>{helperText}</small>}
      </span>
    </label>
  );
}
