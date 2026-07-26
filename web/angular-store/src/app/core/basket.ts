import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../environments/environment';

/**
 * Basket and order API types and access.
 *
 * Owned by this application — the React storefront has its own equivalent in `src/lib/basket.ts`.
 * See docs/adr/0018-self-contained-frontends.md.
 */

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
   * Indicative only. The server re-derives every price from the catalogue when the order is placed,
   * so this can legitimately differ from what is charged.
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

/** The lifecycle, in the order a customer experiences it. Cancelled is not on this path. */
export const ORDER_TIMELINE: readonly OrderStatus[] = [
  'Submitted',
  'AwaitingPayment',
  'Paid',
  'Shipped',
  'Delivered',
];

/**
 * Human wording for each status.
 *
 * The API sends the enum name; the customer reads this. Translating in the UI keeps the wire contract
 * stable when the wording is softened.
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

/** One entry in an order's saga timeline. */
export interface SagaStep {
  readonly name: string;
  readonly detail: string;
  readonly occurredAt: string;
}

/**
 * What the checkout process actually did.
 *
 * Distinct from the order's own status, and both are worth showing. The order says *what it is*
 * ("Cancelled"); the saga says *what happened* ("payment declined, so we released the stock").
 */
export interface SagaTimeline {
  readonly orderId: string;
  readonly orderNumber: string;
  readonly state: 'AwaitingStock' | 'AwaitingPayment' | 'Completed' | 'Compensated' | 'Unknown';
  readonly stockReserved: boolean;
  readonly failureReason: string | null;
  readonly startedAt: string;
  readonly completedAt: string | null;
  readonly steps: readonly SagaStep[];
}

/** Plain-English wording for each saga step. "CompensatingReleaseStock" belongs in a log, not a page. */
export const SAGA_STEP_LABELS: Record<string, string> = {
  OrderSubmitted: 'Order received',
  ReserveStockRequested: 'Checking stock',
  StockReserved: 'Stock reserved for you',
  StockRejected: 'Some items were unavailable',
  PaymentRequested: 'Taking payment',
  PaymentSucceeded: 'Payment successful',
  PaymentFailed: 'Payment declined',
  CompensatingReleaseStock: 'Releasing the reserved stock',
  NoCompensationNeeded: 'Nothing to release',
  SagaCompleted: 'Order confirmed',
  SagaCompensated: 'Order cancelled and stock returned',
};

/**
 * Basket state and API access.
 *
 * ---
 * **React/Angular divergence** (docs/react-vs-angular.md).
 *
 * React holds the basket in a TanStack Query cache keyed `['basket']`, which gives it optimistic
 * updates, automatic rollback and per-mutation pending state for free. Angular holds it in one signal
 * on this service.
 *
 * The signal is genuinely simpler to read, and it is enough here because every endpoint returns the
 * whole basket and there is one consumer. What it does not give you is the rollback: the optimistic
 * update below is hand-written, including saving and restoring the previous value. That is the shape of
 * this comparison throughout — Angular's primitives are smaller and more predictable, and TanStack
 * Query has simply already solved more of the problem.
 */
@Injectable({ providedIn: 'root' })
export class BasketService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.bffBaseUrl}/api/basket/me`;
  private readonly ordersUrl = `${environment.bffBaseUrl}/api/orders`;

  /** The current basket. Null until loaded. */
  readonly basket = signal<Basket | null>(null);

  /** Item count for the header badge. Derived, so it can never disagree with the basket. */
  readonly itemCount = computed(() => this.basket()?.totalUnits ?? 0);

  async load(): Promise<void> {
    this.basket.set(await firstValueFrom(this.http.get<Basket>(this.baseUrl)));
  }

  async add(item: AddToBasketRequest): Promise<void> {
    this.basket.set(await firstValueFrom(this.http.post<Basket>(`${this.baseUrl}/items`, item)));
  }

  /**
   * Sets a line's quantity, optimistically.
   *
   * Changing a quantity is a click a customer repeats several times in a row, and the outcome is
   * completely predictable — setting it to 3 results in 3. So the signal is updated immediately and
   * reconciled with the server's answer afterwards, with the previous value restored on failure.
   *
   * My Account deliberately does NOT do this, because there the server's answer legitimately differs
   * from the request. Optimism is right when you can predict the result and wrong when you cannot.
   */
  async setQuantity(productId: string, quantity: number): Promise<void> {
    const previous = this.basket();

    if (previous) {
      const items = previous.items
        .map((item) =>
          item.productId === productId
            ? { ...item, quantity, lineTotal: item.unitPrice * quantity }
            : item,
        )
        .filter((item) => item.quantity > 0);

      this.basket.set({
        ...previous,
        items,
        estimatedTotal: items.reduce((sum, item) => sum + item.lineTotal, 0),
        totalUnits: items.reduce((sum, item) => sum + item.quantity, 0),
      });
    }

    try {
      // The server's answer is authoritative: it applies the quantity limits and the zero-removes rule,
      // so the guess above is replaced by the truth even when the two agree.
      this.basket.set(
        await firstValueFrom(
          this.http.put<Basket>(`${this.baseUrl}/items/${productId}`, { quantity }),
        ),
      );
    } catch (error) {
      // Optimism without a rollback is just being wrong quickly.
      this.basket.set(previous);
      throw error;
    }
  }

  async remove(productId: string): Promise<void> {
    this.basket.set(
      await firstValueFrom(this.http.delete<Basket>(`${this.baseUrl}/items/${productId}`)),
    );
  }

  async clear(): Promise<void> {
    this.basket.set(await firstValueFrom(this.http.delete<Basket>(this.baseUrl)));
  }

  // --- Orders ---------------------------------------------------------------------------------

  placeOrder(request: PlaceOrderRequest): Promise<Order> {
    return firstValueFrom(this.http.post<Order>(this.ordersUrl, request));
  }

  getMyOrders(page = 1, pageSize = 20): Promise<PagedOrders> {
    return firstValueFrom(
      this.http.get<PagedOrders>(`${this.ordersUrl}/me?page=${page}&pageSize=${pageSize}`),
    );
  }

  getOrder(orderId: string): Promise<Order> {
    return firstValueFrom(this.http.get<Order>(`${this.ordersUrl}/${orderId}`));
  }

  cancelOrder(orderId: string): Promise<Order> {
    return firstValueFrom(this.http.post<Order>(`${this.ordersUrl}/${orderId}/cancel`, {}));
  }

  getSagaTimeline(orderId: string): Promise<SagaTimeline> {
    return firstValueFrom(
      this.http.get<SagaTimeline>(`${environment.bffBaseUrl}/api/saga/orders/${orderId}`),
    );
  }
}
