import type { ReactNode } from 'react';

/**
 * One column of a {@link DataTable}.
 *
 * @typeParam TRow the row type. Generic, so `render` receives a typed row and a renamed field is a
 * compile error rather than an empty cell.
 */
export interface Column<TRow> {
  readonly header: string;
  readonly render: (row: TRow) => ReactNode;
  /** Marks the column that identifies the row. Rendered as `<th scope="row">`. */
  readonly isRowHeader?: boolean;
  /** Right-aligns the column. For numbers, where a ragged right edge is hard to scan. */
  readonly numeric?: boolean;
}

/**
 * The admin panel's table.
 *
 * ---
 * **Why one component rather than a table per page.** Orders, stock, users and the audit log are four
 * screens with the same shape. Written separately, three of them would eventually lose the caption, or
 * the row header, or the empty state — and nobody would notice, because a table with a missing
 * `<caption>` looks identical to one that has it.
 *
 * ---
 * **Accessibility is baked in rather than left to each page.**
 *
 * - a `<caption>`, visually hidden, so a screen reader announces what the table is *before* reading it;
 * - one `<th scope="row">` per row, so each cell is announced with the thing it describes — without it
 *   a screen reader reads "Cancelled" with no idea which order;
 * - `<th scope="col">` headers, so column context is announced too;
 * - a real empty state, because a table with a header and no rows reads as broken.
 *
 * This is the argument for a shared table in one paragraph: **the accessible version is only slightly
 * harder to write, and impossible to keep writing consistently by hand.**
 *
 * ---
 * **Deliberately NOT a generic grid.** No sorting, no column resizing, no virtualisation. Those arrive
 * when a screen needs them; building them first produces a component with twenty props of which four
 * are used.
 */
export function DataTable<TRow>({
  caption,
  columns,
  rows,
  rowKey,
  emptyMessage,
}: {
  caption: string;
  columns: readonly Column<TRow>[];
  rows: readonly TRow[];
  rowKey: (row: TRow) => string;
  emptyMessage: string;
}) {
  if (rows.length === 0) {
    return (
      <div className="card">
        <p className="muted">{emptyMessage}</p>
      </div>
    );
  }

  return (
    <div className="card">
      <table className="table">
        <caption className="visually-hidden">{caption}</caption>
        <thead>
          <tr>
            {columns.map((column) => (
              <th
                key={column.header}
                scope="col"
                style={column.numeric ? { textAlign: 'right' } : undefined}
              >
                {column.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={rowKey(row)}>
              {columns.map((column) =>
                column.isRowHeader ? (
                  <th key={column.header} scope="row">
                    {column.render(row)}
                  </th>
                ) : (
                  <td
                    key={column.header}
                    style={column.numeric ? { textAlign: 'right' } : undefined}
                  >
                    {column.render(row)}
                  </td>
                ),
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
