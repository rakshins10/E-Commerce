/**
 * The admin API.
 *
 * Owned by this application — the Angular admin panel has its own equivalent in `core/admin-api.ts`.
 * See docs/adr/0018-self-contained-frontends.md.
 *
 * Note the base URL: the **admin** BFF on :6002, never the storefront's on :6001. The two gateways are
 * separate on purpose, and pointing this at the wrong one would be a security bug rather than a typo —
 * the storefront BFF does not expose these routes at all.
 */

import { ApiClient } from './api-client';

export interface DashboardStatusCount {
  readonly status: string;
  readonly count: number;
}

export interface Dashboard {
  readonly ordersToday: number;
  readonly ordersTotal: number;
  readonly revenueToday: number;
  readonly revenueTotal: number;
  readonly currency: string;
  readonly ordersInFlight: number;
  readonly ordersCancelled: number;
  /** Sagas that started and never finished. The number somebody should act on. */
  readonly sagasStuck: number;
  readonly lowStockItems: number;
  readonly byStatus: readonly DashboardStatusCount[];
}

export interface AdminOrderSummary {
  readonly id: string;
  readonly orderNumber: string;
  readonly status: string;
  readonly total: number;
  readonly currency: string;
  readonly totalUnits: number;
  readonly placedAt: string;
  readonly canBeCancelled: boolean;
}

export interface AdminOrder {
  readonly id: string;
  readonly orderNumber: string;
  readonly status: string;
  readonly canBeCancelled: boolean;
  readonly total: number;
  readonly currency: string;
  readonly totalUnits: number;
  readonly placedAt: string;
  readonly cancellationReason: string | null;
  readonly shippingAddress: {
    readonly recipient: string;
    readonly line1: string;
    readonly line2: string | null;
    readonly city: string;
    readonly postcode: string;
    readonly country: string;
  };
  readonly items: readonly {
    readonly productId: string;
    readonly sku: string;
    readonly productName: string;
    readonly quantity: number;
    readonly unitPrice: number;
    readonly lineTotal: number;
  }[];
}

export interface StockItem {
  readonly sku: string;
  readonly productName: string;
  readonly onHand: number;
  readonly reserved: number;
  readonly available: number;
  readonly reorderLevel: number;
}

export interface AdminUser {
  readonly id: string;
  readonly username: string;
  readonly email: string | null;
  readonly firstName: string | null;
  readonly lastName: string | null;
  readonly enabled: boolean;
  readonly roles: readonly string[];
}

export interface AuditEntry {
  readonly actorName: string;
  readonly action: string;
  readonly target: string;
  readonly detail: string | null;
  readonly occurredAt: string;
}

export interface SagaStep {
  readonly name: string;
  readonly detail: string;
  readonly occurredAt: string;
}

export interface SagaTimeline {
  readonly orderId: string;
  readonly orderNumber: string;
  readonly state: string;
  readonly stockReserved: boolean;
  readonly failureReason: string | null;
  readonly startedAt: string;
  readonly completedAt: string | null;
  readonly steps: readonly SagaStep[];
}


export interface AdminProduct {
  readonly id: string;
  readonly sku: string;
  readonly name: string;
  readonly description: string;
  readonly price: number;
  readonly currency: string;
  readonly categoryName: string;
  readonly categorySlug: string;
  readonly brandName: string;
  readonly brandSlug: string;
  readonly imageUrl: string | null;
  /** The TOTAL across variants. Per-variant figures are on `variants`. */
  readonly stockOnHand: number;
  readonly audience: string;
  /** Present on the detail response only — the list projection does not carry them. */
  readonly variants?: readonly AdminProductVariant[];
}

/** One sellable size-and-colour. The SKU here is what Inventory holds stock against. */
export interface AdminProductVariant {
  readonly id: string;
  readonly productId: string;
  readonly sku: string;
  readonly size: string | null;
  readonly colourName: string | null;
  readonly colourHex: string | null;
  readonly stockOnHand: number;
}

export interface AdminCategory {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly parentSlug: string | null;
  readonly productCount: number;
}

export interface AdminBrand {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly productCount: number;
}

export interface SaveProductRequest {
  readonly sku?: string;
  readonly name: string;
  readonly description: string;
  readonly price?: number;
  readonly currency?: string;
  readonly categoryId: string;
  readonly brandId: string;
  readonly imageUrl?: string | null;
  readonly audience?: string;
}

/** Roles a manager may assign. Mirrors the server's allow-list — the server is what enforces it. */
export const ASSIGNABLE_ROLES = [
  'customer',
  'support-agent',
  'catalog-manager',
  'order-manager',
  'admin',
] as const;

export function createAdminApi(getAccessToken: () => string | null) {
  const client = new ApiClient({
    baseUrl: import.meta.env.VITE_ADMIN_BFF_URL ?? 'http://localhost:6002',
    getAccessToken,
  });

  return {
    getDashboard: () => client.get<Dashboard>('/api/admin/dashboard'),

    getAuditLog: (limit = 50) => client.get<AuditEntry[]>(`/api/admin/audit?limit=${limit}`),

    // The STAFF endpoint, not /me. /me is always filtered to the caller's own `sub`, so an
    // administrator hitting it would see their own shopping rather than the shop's orders.
    getOrders: (page = 1, pageSize = 25) =>
      client.get<{ items: AdminOrderSummary[]; totalCount: number; totalPages: number; page: number }>(
        `/api/orders?page=${page}&pageSize=${pageSize}`,
      ),

    getOrder: (orderId: string) => client.get<AdminOrder>(`/api/orders/${orderId}`),

    getSagaTimeline: (orderId: string) => client.get<SagaTimeline>(`/api/saga/orders/${orderId}`),

    cancelOrder: (orderId: string) => client.post<AdminOrder>(`/api/orders/${orderId}/cancel`),

    shipOrder: (orderId: string) => client.post<AdminOrder>(`/api/orders/${orderId}/ship`),

    deliverOrder: (orderId: string) => client.post<AdminOrder>(`/api/orders/${orderId}/deliver`),

    getStock: () => client.get<StockItem[]>('/api/inventory'),

    getLowStock: () => client.get<StockItem[]>('/api/inventory/low-stock'),

    adjustStock: (sku: string, delta: number, reason: string) =>
      client.post<StockItem>(`/api/inventory/${sku}/adjust`, { delta, reason }),

    searchUsers: (search?: string) =>
      client.get<AdminUser[]>(`/api/admin/users${search ? `?search=${encodeURIComponent(search)}` : ''}`),

    getUser: (userId: string) => client.get<AdminUser>(`/api/admin/users/${userId}`),

    setUserEnabled: (userId: string, enabled: boolean) =>
      client.post<AdminUser>(`/api/admin/users/${userId}/${enabled ? 'enable' : 'disable'}`),

    assignRole: (userId: string, role: string) =>
      client.post<AdminUser>(`/api/admin/users/${userId}/roles`, { role }),

    removeRole: (userId: string, role: string) =>
      client.delete<AdminUser>(`/api/admin/users/${userId}/roles/${role}`),

    // --- Catalogue ---------------------------------------------------------------------------
    getProducts: (search?: string) =>
      client.get<{ items: AdminProduct[]; totalCount: number }>(
        `/api/catalog/products?pageSize=100${search ? `&search=${encodeURIComponent(search)}` : ''}`,
      ),

    getProduct: (id: string) => client.get<AdminProduct>(`/api/catalog/products/${id}`),

    /** Withdrawn products are invisible to every other query, so they need their own. */
    getWithdrawnProducts: () => client.get<AdminProduct[]>('/api/catalog/products/withdrawn'),

    getCategories: () => client.get<AdminCategory[]>('/api/catalog/categories'),

    getBrands: () => client.get<AdminBrand[]>('/api/catalog/brands'),

    createProduct: (product: SaveProductRequest) =>
      client.post<AdminProduct>('/api/catalog/products', product),

    updateProduct: (id: string, product: SaveProductRequest) =>
      client.put<AdminProduct>(`/api/catalog/products/${id}`, product),

    /** A SEPARATE call, because it needs a separate permission. See ProductAdminEndpoints. */
    changePrice: (id: string, price: number) =>
      client.put<AdminProduct>(`/api/catalog/products/${id}/price`, { price }),

    /** Soft delete. The row survives so historic orders keep working. */
    withdrawProduct: (id: string) => client.delete<void>(`/api/catalog/products/${id}`),

    restoreProduct: (id: string) => client.post<AdminProduct>(`/api/catalog/products/${id}/restore`),
  };
}
