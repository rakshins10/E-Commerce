/**
 * Admin API types.
 *
 * Owned by this application — the React admin panel has its own equivalent in `lib/admin-api.ts`.
 * See docs/adr/0018-self-contained-frontends.md.
 *
 * Note the base URL: the **admin** BFF on :6002, never the storefront's on :6001. The two gateways are
 * separate on purpose, and pointing this at the wrong one would be a security bug rather than a typo —
 * the storefront BFF does not expose these routes at all.
 */

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

/** Roles a manager may assign. Mirrors the server's allow-list — the server is what enforces it. */
export const ASSIGNABLE_ROLES = [
  'customer',
  'support-agent',
  'catalog-manager',
  'order-manager',
  'admin',
] as const;
