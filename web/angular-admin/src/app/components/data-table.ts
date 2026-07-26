import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * One column of a {@link DataTable}.
 *
 * `render` returns a **string**, unlike React's version which returns a `ReactNode`. Angular templates
 * cannot embed arbitrary markup returned from a function, so cells needing links or buttons are
 * projected instead — see the `cellTemplate` note below.
 */
export interface Column<TRow> {
  readonly header: string;
  readonly render: (row: TRow) => string;
  /** Marks the column that identifies the row. Rendered as `<th scope="row">`. */
  readonly isRowHeader?: boolean;
  /** Right-aligns the column. For numbers, where a ragged right edge is hard to scan. */
  readonly numeric?: boolean;
}

/**
 * The back office's table.
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
 * **The accessible version is only slightly harder to write, and impossible to keep writing
 * consistently by hand.** That is the whole argument for a shared table.
 *
 * ---
 * **React/Angular divergence** (docs/react-vs-angular.md).
 *
 * React's `render` returns a `ReactNode`, so a cell can contain a link or a button with no ceremony.
 * Angular has no equivalent — a function cannot return markup — so pages needing interactive cells
 * render their own table rather than contorting this one. Content projection with `ngTemplateOutlet`
 * would work and costs more machinery than the two screens that need it justify.
 *
 * A genuine point for JSX: **"a value that is markup" is a primitive React has and Angular does not.**
 */
@Component({
  selector: 'app-data-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (rows().length === 0) {
      <div class="card">
        <p class="muted">{{ emptyMessage() }}</p>
      </div>
    } @else {
      <div class="card">
        <table class="table">
          <caption class="visually-hidden">{{ caption() }}</caption>
          <thead>
            <tr>
              @for (column of columns(); track column.header) {
                <th scope="col" [style.text-align]="column.numeric ? 'right' : null">
                  {{ column.header }}
                </th>
              }
            </tr>
          </thead>
          <tbody>
            @for (row of rows(); track rowKey()(row)) {
              <tr>
                @for (column of columns(); track column.header) {
                  @if (column.isRowHeader) {
                    <th scope="row">{{ column.render(row) }}</th>
                  } @else {
                    <td [style.text-align]="column.numeric ? 'right' : null">
                      {{ column.render(row) }}
                    </td>
                  }
                }
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class DataTable<TRow> {
  readonly caption = input.required<string>();

  readonly columns = input.required<readonly Column<TRow>[]>();

  readonly rows = input.required<readonly TRow[]>();

  readonly rowKey = input.required<(row: TRow) => string>();

  readonly emptyMessage = input.required<string>();
}
