import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../environments/environment';
import type {
  AdminOrder,
  AdminOrderSummary,
  AdminUser,
  AuditEntry,
  Dashboard,
  SagaTimeline,
  StockItem,
} from './admin-types';

/**
 * The admin API.
 *
 * Owned by this application — the React admin panel has its own equivalent in `lib/admin-api.ts`.
 * See docs/adr/0018-self-contained-frontends.md.
 *
 * ---
 * **React/Angular divergence** (docs/react-vs-angular.md).
 *
 * React returns a plain object of functions from `createAdminApi(getToken)`; Angular uses an injectable
 * whose token is attached by an HTTP interceptor. Angular's version cannot forget the token on a new
 * call site, which is a genuine point for its DI model — but it also means the token attachment is
 * invisible from here, so a reader has to know the interceptor exists.
 */
@Injectable({ providedIn: 'root' })
export class AdminApi {
  private readonly http = inject(HttpClient);

  // The ADMIN BFF, never the storefront's. Pointing this at :6001 would be a security bug rather than
  // a typo - the storefront gateway does not expose these routes at all.
  private readonly baseUrl = environment.adminBffBaseUrl;

  getDashboard(): Promise<Dashboard> {
    return firstValueFrom(this.http.get<Dashboard>(`${this.baseUrl}/api/admin/dashboard`));
  }

  getAuditLog(limit = 50): Promise<AuditEntry[]> {
    return firstValueFrom(this.http.get<AuditEntry[]>(`${this.baseUrl}/api/admin/audit?limit=${limit}`));
  }

  getOrders(page = 1, pageSize = 25): Promise<{
    items: AdminOrderSummary[];
    totalCount: number;
    totalPages: number;
    page: number;
  }> {
    // Staff read ANY order, so this is the same endpoint the storefront uses - the difference is the
    // permission in the token, which drops the buyer filter server-side rather than in the client.
    return firstValueFrom(
      this.http.get<{ items: AdminOrderSummary[]; totalCount: number; totalPages: number; page: number }>(
        `${this.baseUrl}/api/orders/me?page=${page}&pageSize=${pageSize}`,
      ),
    );
  }

  getOrder(orderId: string): Promise<AdminOrder> {
    return firstValueFrom(this.http.get<AdminOrder>(`${this.baseUrl}/api/orders/${orderId}`));
  }

  getSagaTimeline(orderId: string): Promise<SagaTimeline> {
    return firstValueFrom(this.http.get<SagaTimeline>(`${this.baseUrl}/api/saga/orders/${orderId}`));
  }

  cancelOrder(orderId: string): Promise<AdminOrder> {
    return firstValueFrom(
      this.http.post<AdminOrder>(`${this.baseUrl}/api/orders/${orderId}/cancel`, {}),
    );
  }

  shipOrder(orderId: string): Promise<AdminOrder> {
    return firstValueFrom(this.http.post<AdminOrder>(`${this.baseUrl}/api/orders/${orderId}/ship`, {}));
  }

  deliverOrder(orderId: string): Promise<AdminOrder> {
    return firstValueFrom(
      this.http.post<AdminOrder>(`${this.baseUrl}/api/orders/${orderId}/deliver`, {}),
    );
  }

  getStock(): Promise<StockItem[]> {
    return firstValueFrom(this.http.get<StockItem[]>(`${this.baseUrl}/api/inventory`));
  }

  adjustStock(sku: string, delta: number, reason: string): Promise<StockItem> {
    return firstValueFrom(
      this.http.post<StockItem>(`${this.baseUrl}/api/inventory/${sku}/adjust`, { delta, reason }),
    );
  }

  searchUsers(search?: string): Promise<AdminUser[]> {
    const query = search ? `?search=${encodeURIComponent(search)}` : '';
    return firstValueFrom(this.http.get<AdminUser[]>(`${this.baseUrl}/api/admin/users${query}`));
  }

  getUser(userId: string): Promise<AdminUser> {
    return firstValueFrom(this.http.get<AdminUser>(`${this.baseUrl}/api/admin/users/${userId}`));
  }

  setUserEnabled(userId: string, enabled: boolean): Promise<AdminUser> {
    return firstValueFrom(
      this.http.post<AdminUser>(
        `${this.baseUrl}/api/admin/users/${userId}/${enabled ? 'enable' : 'disable'}`,
        {},
      ),
    );
  }

  assignRole(userId: string, role: string): Promise<AdminUser> {
    return firstValueFrom(
      this.http.post<AdminUser>(`${this.baseUrl}/api/admin/users/${userId}/roles`, { role }),
    );
  }

  removeRole(userId: string, role: string): Promise<AdminUser> {
    return firstValueFrom(
      this.http.delete<AdminUser>(`${this.baseUrl}/api/admin/users/${userId}/roles/${role}`),
    );
  }
}
