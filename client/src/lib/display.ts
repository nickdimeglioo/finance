export function displayEnum(value?: string | null): string {
  if (!value) {
    return '';
  }

  return value
    .split('_')
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ');
}
