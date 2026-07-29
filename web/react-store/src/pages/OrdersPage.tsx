import { useMemo } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { useAuth } from 'react-oidc-context';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  createShopApi,
  ORDER_CANCELLATION_LABELS,
  ORDER_STATUS_LABELS,
  type OrderStatus,
} from '../lib/basket';
import { formatDateTime, formatMoney } from '../lib/formatting';
import { useCurrentUser } from '../auth/useCurrentUser';

/** The lifecycle, in the order a customer experiences it. Cancelled is not on this path. */
const TIMELINE: readonly OrderStatus[] = ['Submitted', 'AwaitingPayment', 'Paid', 'Shipped', 'Delivered'];

function SignInPrompt({ title, message }: { title: string; message: string }) {
  const auth = useAuth();

  return (
    <div className="centred">
      <div className="card stack">
        <h1 className="page-title">{title}</h1>
        <p className="muted">{message}</p>
        <div>
          <button type="button" className="btn btn--primary" onClick={() => void auth.signinRedirect()}>
            Sign in
          </button>
        </div>
      </div>
    </div>
  );
}

/** "My orders". */
export function OrdersPage() {
  const auth = useAuth();
  const { isAuthenticated, isLoading: authLoading } = useCurrentUser();

  const api = useMemo(
    () => createShopApi(() => auth.user?.access_token ?? null),
    [auth.user?.access_token],
  );

  const ordersQuery = useQuery({
    queryKey: ['orders'],
    queryFn: () => api.getMyOrders(),
    enabled: isAuthenticated,
  });

  if (authLoading) {
    return (
      <div className="centred" aria-busy="true" aria-live="polite">
        <p className="lede">Loading…</p>
      </div>
    );
  }

  if (!isAuthenticated) {
    return <SignInPrompt title="Your orders" message="Sign in to see your order history." />;
  }

  if (ordersQuery.isPending) {
    return (
      <div className="centred" aria-busy="true" aria-live="polite">
        <p className="lede">Loading your orders…</p>
      </div>
    );
  }

  if (ordersQuery.isError) {
    return (
      <div className="centred">
        <div className="card stack" role="alert">
          <h1 className="page-title">Could not load your orders</h1>
          <p className="muted">{(ordersQuery.error as Error).message}</p>
          <button type="button" className="btn btn--primary" onClick={() => ordersQuery.refetch()}>
            Try again
          </button>
        </div>
      </div>
    );
  }

  const orders = ordersQuery.data;

  return (
    <div className="stack">
      <h1 className="page-title">Your orders</h1>

      {orders.items.length === 0 ? (
        <div className="card stack">
          <p className="lede">You have not placed any orders yet.</p>
          <div>
            <Link className="btn btn--primary" to="/products">
              Browse products
            </Link>
          </div>
        </div>
      ) : (
        <div className="card">
          <table className="table">
            <caption className="visually-hidden">Your orders, newest first</caption>
            <thead>
              <tr>
                <th scope="col">Order</th>
                <th scope="col">Placed</th>
                <th scope="col">Items</th>
                <th scope="col">Total</th>
                <th scope="col">Status</th>
              </tr>
            </thead>
            <tbody>
              {orders.items.map((order) => (
                <tr key={order.id}>
                  <th scope="row">
                    <Link to={`/orders/${order.id}`}>{order.orderNumber}</Link>
                  </th>
                  <td>{formatDateTime(order.placedAt)}</td>
                  <td>{order.totalUnits}</td>
                  <td>{formatMoney({ amount: order.total, currency: order.currency })}</td>
                  <td>{ORDER_STATUS_LABELS[order.status]}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

/** One order, with its status timeline. */
export function OrderDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [searchParams] = useSearchParams();
  const auth = useAuth();
  const queryClient = useQueryClient();
  const { isAuthenticated, isLoading: authLoading } = useCurrentUser();

  // Set by checkout on redirect, so a customer arriving straight from placing an order gets a
  // confirmation rather than a bare detail page that gives no sign anything succeeded.
  const justPlaced = searchParams.get('placed') === '1';

  const api = useMemo(
    () => createShopApi(() => auth.user?.access_token ?? null),
    [auth.user?.access_token],
  );

  const orderQuery = useQuery({
    queryKey: ['order', id],
    queryFn: () => api.getOrder(id!),
    enabled: isAuthenticated && Boolean(id),
  });

  const cancel = useMutation({
    mutationFn: () => api.cancelOrder(id!),
    onSuccess: (updated) => {
      queryClient.setQueryData(['order', id], updated);
      // The list shows status and cancellability, so it is stale the moment this succeeds.
      void queryClient.invalidateQueries({ queryKey: ['orders'] });
    },
  });

  if (authLoading) {
    return (
      <div className="centred" aria-busy="true" aria-live="polite">
        <p className="lede">Loading…</p>
      </div>
    );
  }

  if (!isAuthenticated) {
    return <SignInPrompt title="Order" message="Sign in to see this order." />;
  }

  if (orderQuery.isPending) {
    return (
      <div className="centred" aria-busy="true" aria-live="polite">
        <p className="lede">Loading your order…</p>
      </div>
    );
  }

  if (orderQuery.isError) {
    return (
      <div className="centred">
        <div className="card stack" role="alert">
          <h1 className="page-title">Order not found</h1>
          <p className="muted">
            We could not find that order. It may belong to a different account.
          </p>
          <Link className="btn btn--primary" to="/orders">
            Your orders
          </Link>
        </div>
      </div>
    );
  }

  const order = orderQuery.data;
  const isCancelled = order.status === 'Cancelled';
  const reachedIndex = TIMELINE.indexOf(order.status);

  return (
    <div className="stack">
      {justPlaced && (
        <div className="card" role="status">
          <p className="lede">Thank you — your order is confirmed.</p>
          <p className="muted">We have sent the details to your email address.</p>
        </div>
      )}

      <h1 className="page-title">Order {order.orderNumber}</h1>

      <section className="card stack" aria-labelledby="status-heading">
        <h2 id="status-heading">Status</h2>

        <p className="lede" role="status">
          {ORDER_STATUS_LABELS[order.status]}
        </p>

        {isCancelled ? (
          <p className="muted">
            {order.cancellationReason
              ? (ORDER_CANCELLATION_LABELS[order.cancellationReason] ?? 'This order was cancelled')
              : 'This order was cancelled'}
            {order.cancelledAt ? ` on ${formatDateTime(order.cancelledAt)}` : ''}.
          </p>
        ) : (
          // An ordered list, because these steps have an order and a screen reader should announce it.
          // A row of styled divs would read as unrelated words.
          <ol className="timeline">
            {TIMELINE.map((step, index) => (
              <li
                key={step}
                className={index <= reachedIndex ? 'timeline__step is-done' : 'timeline__step'}
              >
                <span>{ORDER_STATUS_LABELS[step]}</span>
                {index <= reachedIndex && <span className="visually-hidden"> — completed</span>}
              </li>
            ))}
          </ol>
        )}

        {order.canBeCancelled && (
          <div>
            <button
              type="button"
              className="btn btn--secondary"
              onClick={() => cancel.mutate()}
              disabled={cancel.isPending}
            >
              {cancel.isPending ? 'Cancelling…' : 'Cancel order'}
            </button>
            {/* The button is hidden once the aggregate says no. Hiding it is a courtesy; the server
                refusing it is the actual rule - a dispatched order needs a return, not a cancellation. */}
          </div>
        )}

        {cancel.isError && (
          <p className="muted" role="alert">
            {(cancel.error as Error).message}
          </p>
        )}
      </section>

      <section className="card stack" aria-labelledby="items-heading">
        <h2 id="items-heading">Items</h2>

        <table className="table">
          <thead>
            <tr>
              <th scope="col">Product</th>
              <th scope="col">Quantity</th>
              <th scope="col">Price</th>
              <th scope="col">Total</th>
            </tr>
          </thead>
          <tbody>
            {order.items.map((item) => (
              <tr key={item.productId}>
                <th scope="row">{item.productName}</th>
                <td>{item.quantity}</td>
                <td>{formatMoney({ amount: item.unitPrice, currency: order.currency })}</td>
                <td>{formatMoney({ amount: item.lineTotal, currency: order.currency })}</td>
              </tr>
            ))}
          </tbody>
        </table>

        <p className="lede">
          Total: {formatMoney({ amount: order.total, currency: order.currency })}
        </p>
      </section>

      <section className="card stack" aria-labelledby="delivery-heading">
        <h2 id="delivery-heading">Delivery address</h2>
        <address>
          {order.shippingAddress.recipient}
          <br />
          {order.shippingAddress.line1}
          <br />
          {order.shippingAddress.line2 && (
            <>
              {order.shippingAddress.line2}
              <br />
            </>
          )}
          {order.shippingAddress.city}
          <br />
          {order.shippingAddress.postcode}
          <br />
          {order.shippingAddress.country}
        </address>
        <p className="muted small">
          Recorded as it was when you ordered, so changing your address book will not alter this.
        </p>
      </section>
    </div>
  );
}
