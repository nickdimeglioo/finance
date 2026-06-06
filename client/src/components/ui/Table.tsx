import type { ReactNode } from 'react';

export interface TableColumn<T> {
  key: string;
  label: string;
  width?: number;
  align?: 'left' | 'right' | 'center';
  render?: (row: T) => ReactNode;
}

interface TableProps<T> {
  data: T[];
  columns: TableColumn<T>[];
  rowKey: keyof T | ((row: T, index: number) => string | number);
  loading?: boolean;
  emptyMessage?: string;
}

export function Table<T>({ data, columns, rowKey, loading = false, emptyMessage = 'No records found.' }: TableProps<T>) {
  const resolveKey = (row: T, index: number) =>
    typeof rowKey === 'function' ? rowKey(row, index) : String(row[rowKey]);

  if (loading) {
    return <div className="pm-loading">Loading...</div>;
  }

  if (data.length === 0) {
    return <div className="pm-empty">{emptyMessage}</div>;
  }

  return (
    <div className="pm-table-shell">
      <div className="pm-table-wrap">
        <table className="pm-table">
          <thead>
            <tr>
              {columns.map((column) => (
                <th
                  key={column.key}
                  className={`pm-table-head-cell align-${column.align ?? 'left'}`}
                  style={column.width ? { width: column.width } : undefined}
                >
                  {column.label}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {data.map((row, index) => (
              <tr key={resolveKey(row, index)} className="pm-table-row">
                {columns.map((column) => (
                  <td key={column.key} className={`pm-table-cell align-${column.align ?? 'left'}`} data-label={column.label}>
                    {column.render ? column.render(row) : String((row as Record<string, unknown>)[column.key] ?? '')}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
