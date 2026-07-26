/**
 * Basket and order API types and calls.
 *
 * Owned by this application — the Angular storefront has its own equivalent in
 * `core/basket.ts`. See docs/adr/0018-self-contained-frontends.md.
 */

import { ApiClient } from './api-client';

export interface BasketItem {
  readonly productId: string;
  readonly sku: string;
  readonly productName: string;
  readonly imageUrl: string | null;
  readonly unitPrice: number;
  readonly currency: string;
  readonly quantity: number;
  readonly lineTotal: number;
}

export interface Basket {
  readonly buyerId: string;
  readonly items: readonly BasketItem[];
  /**
   * Indicative only.
   *
   * The server re-derives every price from the catalogue when the order is placed, so this can
   * legitimately differ from what is charged — a basket may have sat for a month. The checkout page
   * says so rather than presenting it as a promise.
   */
  readonly estimatedTotal: number;
  readonly totalUnits: number;
  readonly currency: string;
}

export interface AddToBasketRequest {
  readonly productId: string;
  readonly sku: string;
  readonly productName: string;
  readonly imageUrl?: string | null;
  readonly unitPrice: number;
  readonly currency: string;
  readonly quantity: number;
}

export interface OrderItem {
  readonly productId: string;
  readonly sku: string;
  readonly productName: string;
  readonly quantity: number;
  readonly unitPrice: number;
  readonly lineTotal: number;
}

export interface OrderAddress {
  readonly recipient: string;
  readonly line1: string;
  readonly line2: string | null;
  readonly city: string;
  readonly postcode: string;
  readonly country: string;
}

export type OrderStatus =
  | 'Submitted'
  | 'AwaitingPayment'
  | 'Paid'
  | 'Shipped'
  | 'Delivered'
  | 'Cancelled';

export interface Order {
  readonly id: string;
  readonly orderNumber: string;
  readonly status: OrderStatus;
  /** Decided by the aggregate, not by the client. The UI only honours it. */
  readonly canBeCancelled: boolean;
  readonly total: number;
  readonly currency: string;
  readonly totalUnits: number;
  readonly placedAt: string;
  readonly paidAt: string | null;
  readonly shippedAt: string | null;
  readonly deliveredAt: string | null;
  readonly cancelledAt: string | null;
  readonly cancellationReason: string | null;
  readonly shippingAddress: OrderAddress;
  readonly items: readonly OrderItem[];
}

export interface OrderSummary {
  readonly id: string;
  readonly orderNumber: string;
  readonly status: OrderStatus;
  readonly total: number;
  readonly currency: string;
  readonly totalUnits: number;
  readonly placedAt: string;
  readonly canBeCancelled: boolean;
}

export interface PagedOrders {
  readonly items: readonly OrderSummary[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
}

export interface PlaceOrderRequest {
  readonly shippingAddress: OrderAddress;
  readonly currency: string;
}

/**
 * Human wording for each status.
 *
 * The API sends the enum name; the customer reads this. Translating in the UI rather than the API keeps
 * the wire contract stable when the wording is softened, and means the same status can read differently
 * on a list and on a detail page if that ever helps.
 */
export const ORDER_STATUS_LABELS: Record<OrderStatus, string> = {
  Submitted: 'Order placed',
  AwaitingPayment: 'Awaiting payment',
  Paid: 'Paid',
  Shipped: 'On its way',
  Delivered: 'Delivered',
  Cancelled: 'Cancelled',
};

export const ORDER_CANCELLATION_LABELS: Record<string, string> = {
  RequestedByCustomer: 'You cancelled this order',
  CancelledByStaff: 'Cancelled by our team',
  PaymentDeclined: 'Payment was declined',
  OutOfStock: 'An item went out of stock',
};

export function createShopApi(getAccessToken: () => string | null) {
  const client = new ApiClient({
    baseUrl: import.meta.env.VITE_BFF_URL ?? 'http://localhost:6001',
    getAccessToken,
  });

  return {
    getBasket: () => client.get<Basket>('/api/basket/me'),

    addToBasket: (item: AddToBasketRequest) =>
      client.post<Basket>('/api/basket/me/items', item),

    /** Quantity 0 removes the line — the server treats it that way, so the UI need not special-case it. */
    setQuantity: (productId: string, quantity: number) =>
      client.put<Basket>(`/api/basket/me/items/${productId}`, { quantity }),

    removeFromBasket: (productId: string) =>
      client.delete<Basket>(`/api/basket/me/items/${productId}`),

    clearBasket: () => client.delete<Basket>('/api/basket/me'),

    placeOrder: (request: PlaceOrderRequest) =>
      client.post<Order>('/api/orders', request),

    getMyOrders: (page = 1, pageSize = 20) =>
      client.get<PagedOrders>(`/api/orders/me?page=${page}&pageSize=${pageSize}`),

    getOrder: (orderId: string) => client.get<Order>(`/api/orders/${orderId}`),

    cancelOrder: (orderId: string) => client.post<Order>(`/api/orders/${orderId}/cancel`),
  };
}
