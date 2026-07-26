import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { Auth } from '../auth/auth';
import { AdminApi } from '../core/admin-api';
import { formatDateTime } from '../core/formatting';
import type { AuditEntry } from '../core/admin-types';

/** The audit log. Append-only, newest first. */
@Component({
  selector: 'app-audit-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (isLoading()) {
      <div class="centred" aria-busy="true" aria-live="polite">
        <p class="lede">Loading the audit log…</p>
      </div>
    } @else if (error()) {
      <div class="card stack" role="alert">
        <h2>Could not load this</h2>
        <p class="muted">{{ error() }}</p>
        <div><button type="button" class="btn btn--primary" (click)="reload()">Try again</button></div>
      </div>
    } @else {
      <div class="stack">
        <h1 class="page-title">Audit log</h1>

        <p class="muted small">
          Append-only. Entries record human decisions — an order the saga cancelled is not audited, an
          order a manager cancelled is.
        </p>

        @if (entries().length === 0) {
          <div class="card"><p class="muted">Nothing has been recorded yet.</p></div>
        } @else {
          <div class="card">
            <table class="table">
              <caption class="visually-hidden">Recent staff actions, newest first</caption>
              <thead>
                <tr>
                  <th scope="col">When</th>
                  <th scope="col">Who</th>
                  <th scope="col">Did</th>
                  <th scope="col">To</th>
                  <th scope="col">Detail</th>
                </tr>
              </thead>
              <tbody>
                @for (entry of entries(); track $index) {
                  <tr>
                    <th scope="row">{{ dateTime(entry.occurredAt) }}</th>
                    <td>{{ entry.actorName }}</td>
                    <td>{{ entry.action }}</td>
                    <td>{{ entry.target }}</td>
                    <td>{{ entry.detail ?? '—' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>
    }
  `,
})
export class AuditPage {
  private readonly api = inject(AdminApi);

  protected readonly entries = signal<readonly AuditEntry[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly dateTime = (value: string) => formatDateTime(value);

  constructor() {
    void this.reload();
  }

  protected async reload(): Promise<void> {
    this.isLoading.set(true);
    this.error.set(null);

    try {
      this.entries.set(await this.api.getAuditLog());
    } catch (cause) {
      this.error.set(cause instanceof Error ? cause.message : 'Something went wrong.');
    } finally {
      this.isLoading.set(false);
    }
  }
}

/**
 * Where a permission guard sends you.
 *
 * A distinct page rather than an inline message, because a distinct URL is linkable and appears in
 * analytics — "twelve people a day hit /forbidden on /users" is a fact somebody can act on.
 */
@Component({
  selector: 'app-forbidden-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <div class="centred">
      <div class="card stack" role="alert">
        <h1 class="page-title">You do not have access to this</h1>
        <p class="muted">
          Your account does not hold the permission this page needs. If you think it should, ask an
          administrator to check your roles.
        </p>
        <div><a class="btn btn--primary" routerLink="/">Back to the dashboard</a></div>
      </div>
    </div>
  `,
})
export class ForbiddenPage {}

@Component({
  selector: 'app-signin-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="centred">
      <div class="card stack">
        <h1 class="page-title">Back office</h1>
        <p class="muted">Sign in with your staff account.</p>
        <div><button type="button" class="btn btn--primary" (click)="auth.signIn()">Sign in</button></div>
      </div>
    </div>
  `,
})
export class SignInPage {
  protected readonly auth = inject(Auth);
}

@Component({
  selector: 'app-not-found-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <div class="centred">
      <div class="card stack">
        <h1 class="page-title">Page not found</h1>
        <div><a class="btn btn--primary" routerLink="/">Back to the dashboard</a></div>
      </div>
    </div>
  `,
})
export class NotFoundPage {}

/** The OIDC redirect target. The library restores the pre-login route itself. */
@Component({
  selector: 'app-auth-callback-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="centred" aria-busy="true" aria-live="polite">
      <p class="lede">Completing sign-in…</p>
    </div>
  `,
})
export class AuthCallbackPage {}
