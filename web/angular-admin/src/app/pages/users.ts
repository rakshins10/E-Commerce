import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { Auth } from '../auth/auth';
import { AdminApi } from '../core/admin-api';
import { ASSIGNABLE_ROLES } from '../core/admin-types';
import { Permissions } from '../core/permissions';
import type { AdminUser } from '../core/admin-types';

/** User search and enable/disable. */
@Component({
  selector: 'app-users-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink],
  template: `
    <div class="stack">
      <h1 class="page-title">Users</h1>

      <div class="card">
        <div class="field">
          <label for="user-search">Search</label>
          <input
            id="user-search"
            type="search"
            class="input"
            placeholder="Username or email"
            [ngModel]="search()"
            (ngModelChange)="onSearch($event)"
          />
        </div>
      </div>

      @if (isLoading()) {
        <div class="centred" aria-busy="true" aria-live="polite">
          <p class="lede">Loading users…</p>
        </div>
      } @else if (error()) {
        <div class="card stack" role="alert">
          <h2>Could not load this</h2>
          <p class="muted">{{ error() }}</p>
          <div><button type="button" class="btn btn--primary" (click)="reload()">Try again</button></div>
        </div>
      } @else if (users().length === 0) {
        <div class="card"><p class="muted">No users match that search.</p></div>
      } @else {
        <div class="card">
          <table class="table">
            <caption class="visually-hidden">Users in the realm</caption>
            <thead>
              <tr>
                <th scope="col">Username</th>
                <th scope="col">Email</th>
                <th scope="col">Status</th>
                @if (auth.can(canManage)) {
                  <th scope="col">Actions</th>
                }
              </tr>
            </thead>
            <tbody>
              @for (user of users(); track user.id) {
                <tr>
                  <th scope="row"><a [routerLink]="['/users', user.id]">{{ user.username }}</a></th>
                  <td>{{ user.email ?? '—' }}</td>
                  <td>
                    <span [class]="user.enabled ? 'badge badge--ok' : 'badge badge--low'">
                      {{ user.enabled ? 'Enabled' : 'Disabled' }}
                    </span>
                  </td>
                  @if (auth.can(canManage)) {
                    <td>
                      <!-- The server refuses this too. Hiding it saves a pointless round trip and a
                           confusing error; the server refusing it is what actually stops it. -->
                      <button
                        type="button"
                        class="btn btn--secondary"
                        [disabled]="saving() || (isSelf(user) && user.enabled)"
                        [title]="isSelf(user) && user.enabled ? 'You cannot disable your own account' : ''"
                        (click)="toggle(user)"
                      >
                        <span aria-hidden="true">{{ user.enabled ? 'Disable' : 'Enable' }}</span>
                        <span class="visually-hidden">
                          {{ user.enabled ? 'Disable' : 'Enable' }} {{ user.username }}
                        </span>
                      </button>
                    </td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      <p class="muted small">
        Users live in Keycloak, not in this application. Disabling an account here disables the login
        itself.
      </p>
    </div>
  `,
})
export class UsersPage {
  protected readonly auth = inject(Auth);
  private readonly api = inject(AdminApi);

  protected readonly users = signal<readonly AdminUser[]>([]);
  protected readonly search = signal('');
  protected readonly isLoading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly canManage = Permissions.Users.Manage;

  constructor() {
    void this.reload();
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    void this.reload();
  }

  protected isSelf(user: AdminUser): boolean {
    return user.id === this.auth.user()?.id;
  }

  protected async reload(): Promise<void> {
    this.isLoading.set(true);
    this.error.set(null);

    try {
      this.users.set(await this.api.searchUsers(this.search() || undefined));
    } catch (cause) {
      this.error.set(cause instanceof Error ? cause.message : 'Something went wrong.');
    } finally {
      this.isLoading.set(false);
    }
  }

  protected async toggle(user: AdminUser): Promise<void> {
    this.saving.set(true);
    this.error.set(null);

    try {
      await this.api.setUserEnabled(user.id, !user.enabled);
      await this.reload();
    } catch (cause) {
      this.error.set(cause instanceof Error ? cause.message : 'Something went wrong.');
    } finally {
      this.saving.set(false);
    }
  }
}

/** One user, with their roles. */
@Component({
  selector: 'app-user-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  template: `
    @if (isLoading()) {
      <div class="centred" aria-busy="true" aria-live="polite">
        <p class="lede">Loading the user…</p>
      </div>
    } @else if (!user()) {
      <div class="card stack" role="alert">
        <h2>Could not load this</h2>
        <p class="muted">{{ error() ?? 'User not found.' }}</p>
      </div>
    } @else if (user(); as u) {
      <div class="stack">
        <h1 class="page-title">{{ u.username }}</h1>

        <section class="card stack" aria-labelledby="roles-heading">
          <h2 id="roles-heading">Roles</h2>

          <p class="muted small">
            A role is a job title. What it actually permits is a composite in Keycloak, so granting a
            permission to a role reaches everyone who holds it with no deployment.
          </p>

          <ul class="plain-list">
            @if (u.roles.length === 0) {
              <li class="muted">No roles assigned.</li>
            }
            @for (assigned of u.roles; track assigned) {
              <li class="row">
                <span>{{ assigned }}</span>
                @if (auth.can(canManageRoles)) {
                  <button
                    type="button"
                    class="btn btn--secondary"
                    [disabled]="saving()"
                    (click)="revoke(assigned)"
                  >
                    <span aria-hidden="true">Remove</span>
                    <span class="visually-hidden">Remove role {{ assigned }}</span>
                  </button>
                }
              </li>
            }
          </ul>

          @if (auth.can(canManageRoles)) {
            <form class="row" (ngSubmit)="grant()">
              <div class="field">
                <label for="role">Grant a role</label>
                <select id="role" class="input" [ngModel]="role()" (ngModelChange)="role.set($event)" name="role">
                  @for (option of roles; track option) {
                    <option [value]="option">{{ option }}</option>
                  }
                </select>
              </div>
              <button type="submit" class="btn btn--primary" [disabled]="saving()">Grant</button>
            </form>
          }

          @if (error()) {
            <p class="muted" role="alert">{{ error() }}</p>
          }
        </section>
      </div>
    }
  `,
})
export class UserDetailPage {
  readonly id = input.required<string>();

  protected readonly auth = inject(Auth);
  private readonly api = inject(AdminApi);

  protected readonly user = signal<AdminUser | null>(null);
  protected readonly isLoading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly role = signal<string>(ASSIGNABLE_ROLES[0]);

  protected readonly roles = ASSIGNABLE_ROLES;
  protected readonly canManageRoles = Permissions.Users.ManageRoles;

  constructor() {
    // A required signal input is not populated until AFTER the constructor - see orders.ts.
    queueMicrotask(() => void this.load());
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      this.user.set(await this.api.getUser(this.id()));
    } catch (cause) {
      this.error.set(cause instanceof Error ? cause.message : 'Something went wrong.');
      this.user.set(null);
    } finally {
      this.isLoading.set(false);
    }
  }

  protected async grant(): Promise<void> {
    await this.change(() => this.api.assignRole(this.id(), this.role()));
  }

  protected async revoke(role: string): Promise<void> {
    await this.change(() => this.api.removeRole(this.id(), role));
  }

  private async change(action: () => Promise<AdminUser>): Promise<void> {
    this.saving.set(true);
    this.error.set(null);

    try {
      this.user.set(await action());
    } catch (cause) {
      this.error.set(cause instanceof Error ? cause.message : 'Something went wrong.');
    } finally {
      this.saving.set(false);
    }
  }
}
