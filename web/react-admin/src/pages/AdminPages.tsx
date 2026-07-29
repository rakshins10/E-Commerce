import { useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useAuth } from 'react-oidc-context';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { ASSIGNABLE_ROLES, createAdminApi, type AdminOrderSummary, type AdminUser, type AuditEntry, type StockItem } from '../lib/admin-api';
import { formatDateTime, formatMoney } from '../lib/formatting';
import { DataTable, type Column } from '../components/DataTable';
import { useCurrentUser } from '../auth/useCurrentUser';
import { Permissions } from '../lib/permissions';

function useApi() {
  const auth = useAuth();

  return useMemo(
    () => createAdminApi(() => auth.user?.access_token ?? null),
    [auth.user?.access_token],
  );
}

function Loading({ what }: { what: string }) {
  return (
    <div className="centred" aria-busy="true" aria-live="polite">
      <p className="lede">Loading {what}…</p>
    </div>
  );
}

function LoadError({ error, retry }: { error: unknown; retry: () => void }) {
  return (
    <div className="card stack" role="alert">
      <h2>Could not load this</h2>
      <p className="muted">{error instanceof Error ? error.message : 'Something went wrong.'}</p>
      <div>
        <button type="button" className="btn btn--primary" onClick={retry}>
          Try again
        </button>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------------------------
//  Dashboard
// ---------------------------------------------------------------------------------------------

export function DashboardPage() {
  const api = useApi();

  const query = useQuery({ queryKey: ['dashboard'], queryFn: () => api.getDashboard() });

  if (query.isPending) return <Loading what="the dashboard" />;
  if (query.isError) return <LoadError error={query.error} retry={() => query.refetch()} />;

  const data = query.data;

  return (
    <div className="stack">
      <h1 className="page-title">Dashboard</h1>

      <div className="stat-grid">
        <Stat label="Orders today" value={String(data.ordersToday)} />
        <Stat
          label="Revenue today"
          value={formatMoney({ amount: data.revenueToday, currency: data.currency })}
        />
        <Stat label="Orders in flight" value={String(data.ordersInFlight)} />
        <Stat label="Cancelled" value={String(data.ordersCancelled)} />

        {/* The two that matter operationally are marked, because a dashboard where every figure looks
            equally important is a dashboard nobody reads. These are the ones somebody should act on. */}
        <Stat
          label="Sagas stuck"
          value={String(data.sagasStuck)}
          tone={data.sagasStuck > 0 ? 'danger' : undefined}
        />
        <Stat
          label="Low stock items"
          value={String(data.lowStockItems)}
          tone={data.lowStockItems > 0 ? 'warning' : undefined}
        />
      </div>

      <section className="stack" aria-labelledby="by-status-heading">
        <h2 id="by-status-heading">Orders by status</h2>

        <DataTable
          caption="Order counts by status"
          rows={data.byStatus}
          rowKey={(row) => row.status}
          emptyMessage="No orders yet."
          columns={[
            { header: 'Status', render: (row) => row.status, isRowHeader: true },
            { header: 'Count', render: (row) => row.count, numeric: true },
          ]}
        />
      </section>
    </div>
  );
}

function Stat({ label, value, tone }: { label: string; value: string; tone?: 'warning' | 'danger' }) {
  return (
    <div className={tone ? `card stat stat--${tone}` : 'card stat'}>
      <div className="stat__label">{label}</div>
      <div className="stat__value">{value}</div>
    </div>
  );
}

// ---------------------------------------------------------------------------------------------
//  Orders
// ---------------------------------------------------------------------------------------------

export function OrdersPage() {
  const api = useApi();

  const query = useQuery({ queryKey: ['admin-orders'], queryFn: () => api.getOrders() });

  if (query.isPending) return <Loading what="orders" />;
  if (query.isError) return <LoadError error={query.error} retry={() => query.refetch()} />;

  const columns: Column<AdminOrderSummary>[] = [
    {
      header: 'Order',
      isRowHeader: true,
      render: (row) => <Link to={`/orders/${row.id}`}>{row.orderNumber}</Link>,
    },
    { header: 'Placed', render: (row) => formatDateTime(row.placedAt) },
    { header: 'Status', render: (row) => row.status },
    { header: 'Units', render: (row) => row.totalUnits, numeric: true },
    {
      header: 'Total',
      numeric: true,
      render: (row) => formatMoney({ amount: row.total, currency: row.currency }),
    },
  ];

  return (
    <div className="stack">
      <h1 className="page-title">Orders</h1>

      <DataTable
        caption="All orders, newest first"
        rows={query.data.items}
        rowKey={(row) => row.id}
        emptyMessage="No orders have been placed yet."
        columns={columns}
      />
    </div>
  );
}

export function OrderDetailPage() {
  const { id } = useParams<{ id: string }>();
  const api = useApi();
  const queryClient = useQueryClient();
  const { can } = useCurrentUser();

  const query = useQuery({ queryKey: ['admin-order', id], queryFn: () => api.getOrder(id!) });

  const sagaQuery = useQuery({
    queryKey: ['admin-saga', id],
    queryFn: () => api.getSagaTimeline(id!),
    retry: false,
  });

  const advance = useMutation({
    mutationFn: (action: 'ship' | 'deliver' | 'cancel') =>
      action === 'ship'
        ? api.shipOrder(id!)
        : action === 'deliver'
          ? api.deliverOrder(id!)
          : api.cancelOrder(id!),
    onSuccess: (updated) => {
      queryClient.setQueryData(['admin-order', id], updated);
      void queryClient.invalidateQueries({ queryKey: ['admin-orders'] });
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });

  if (query.isPending) return <Loading what="the order" />;
  if (query.isError) return <LoadError error={query.error} retry={() => query.refetch()} />;

  const order = query.data;

  return (
    <div className="stack">
      <h1 className="page-title">Order {order.orderNumber}</h1>

      <section className="card stack" aria-labelledby="status-heading">
        <h2 id="status-heading">Status</h2>
        <p className="lede" role="status">
          {order.status}
          {order.cancellationReason ? ` — ${order.cancellationReason}` : ''}
        </p>

        {/* Each action is gated on its own permission AND on the aggregate's own answer. The order
            manager sees "Mark shipped" on a paid order and nothing on a cancelled one, because the
            server already decided what is legal - the client only honours it. */}
        <div className="row">
          {can(Permissions.Order.Cancel) && order.status === 'Paid' && (
            <button
              type="button"
              className="btn btn--primary"
              disabled={advance.isPending}
              onClick={() => advance.mutate('ship')}
            >
              Mark shipped
            </button>
          )}

          {can(Permissions.Order.Cancel) && order.status === 'Shipped' && (
            <button
              type="button"
              className="btn btn--primary"
              disabled={advance.isPending}
              onClick={() => advance.mutate('deliver')}
            >
              Mark delivered
            </button>
          )}

          {can(Permissions.Order.Cancel) && order.canBeCancelled && (
            <button
              type="button"
              className="btn btn--secondary"
              disabled={advance.isPending}
              onClick={() => advance.mutate('cancel')}
            >
              Cancel order
            </button>
          )}
        </div>

        {advance.isError && (
          <p className="muted" role="alert">
            {(advance.error as Error).message}
          </p>
        )}
      </section>

      {sagaQuery.data && sagaQuery.data.steps.length > 0 && (
        <section className="card stack" aria-labelledby="saga-heading">
          <h2 id="saga-heading">Checkout process</h2>

          {/* The step names are NOT translated here, unlike the storefront. Staff diagnosing a failure
              want the real names - "CompensatingReleaseStock" is precise, and softening it to
              "Releasing stock" loses the signal that this was a compensation. */}
          <ol className="plain-list">
            {sagaQuery.data.steps.map((step, index) => (
              <li key={`${step.name}-${index}`}>
                <strong>{step.name}</strong> — {step.detail}
                <span className="muted small"> · {formatDateTime(step.occurredAt)}</span>
              </li>
            ))}
          </ol>

          {sagaQuery.data.failureReason && (
            <p className="muted">Failure: {sagaQuery.data.failureReason}</p>
          )}
        </section>
      )}

      <section className="stack" aria-labelledby="items-heading">
        <h2 id="items-heading">Items</h2>

        <DataTable
          caption={`Items on order ${order.orderNumber}`}
          rows={order.items}
          rowKey={(row) => row.productId}
          emptyMessage="This order has no items."
          columns={[
            { header: 'Product', isRowHeader: true, render: (row) => row.productName },
            { header: 'SKU', render: (row) => row.sku },
            { header: 'Qty', numeric: true, render: (row) => row.quantity },
            {
              header: 'Unit price',
              numeric: true,
              render: (row) => formatMoney({ amount: row.unitPrice, currency: order.currency }),
            },
            {
              header: 'Line total',
              numeric: true,
              render: (row) => formatMoney({ amount: row.lineTotal, currency: order.currency }),
            },
          ]}
        />

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
          {order.shippingAddress.city}, {order.shippingAddress.postcode}
          <br />
          {order.shippingAddress.country}
        </address>
      </section>
    </div>
  );
}

// ---------------------------------------------------------------------------------------------
//  Inventory
// ---------------------------------------------------------------------------------------------

export function InventoryPage() {
  const api = useApi();
  const queryClient = useQueryClient();
  const { can } = useCurrentUser();
  const [adjusting, setAdjusting] = useState<string | null>(null);
  const [delta, setDelta] = useState('0');
  const [reason, setReason] = useState('');

  const query = useQuery({ queryKey: ['stock'], queryFn: () => api.getStock() });

  const adjust = useMutation({
    mutationFn: () => api.adjustStock(adjusting!, Number(delta), reason),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['stock'] });
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] });
      setAdjusting(null);
      setDelta('0');
      setReason('');
    },
  });

  if (query.isPending) return <Loading what="stock" />;
  if (query.isError) return <LoadError error={query.error} retry={() => query.refetch()} />;

  const columns: Column<StockItem>[] = [
    { header: 'SKU', isRowHeader: true, render: (row) => row.sku },
    { header: 'Product', render: (row) => row.productName },
    { header: 'On hand', numeric: true, render: (row) => row.onHand },
    { header: 'Reserved', numeric: true, render: (row) => row.reserved },
    {
      header: 'Available',
      numeric: true,
      render: (row) => (
        // Text, not just colour. WCAG 1.4.1 - a red number is invisible to a colour-blind user, so the
        // word "Low" carries the meaning and the colour merely reinforces it.
        <>
          {row.available}
          {row.available <= row.reorderLevel && <span className="badge badge--low"> Low</span>}
        </>
      ),
    },
  ];

  if (can(Permissions.Inventory.Adjust)) {
    columns.push({
      header: 'Adjust',
      render: (row) => (
        <button
          type="button"
          className="btn btn--secondary"
          onClick={() => setAdjusting(row.sku)}
        >
          <span aria-hidden="true">Adjust</span>
          <span className="visually-hidden">Adjust stock for {row.productName}</span>
        </button>
      ),
    });
  }

  return (
    <div className="stack">
      <h1 className="page-title">Inventory</h1>

      <p className="muted small">
        On hand is what is physically on the shelf. Reserved is spoken for by an order that has not
        shipped — still on the shelf. Available is what a new order may take.
      </p>

      {adjusting && (
        <form
          className="card stack"
          onSubmit={(event) => {
            event.preventDefault();
            adjust.mutate();
          }}
        >
          <h2>Adjust {adjusting}</h2>

          <div className="field">
            <label htmlFor="delta">Change (negative to reduce)</label>
            <input
              id="delta"
              type="number"
              className="input input--narrow"
              value={delta}
              onChange={(event) => setDelta(event.target.value)}
            />
          </div>

          <div className="field">
            <label htmlFor="reason">Reason</label>
            {/* Required, because an unexplained stock movement is impossible to audit later. */}
            <input
              id="reason"
              className="input"
              required
              maxLength={200}
              placeholder="Goods in, damage, stock take…"
              value={reason}
              onChange={(event) => setReason(event.target.value)}
            />
          </div>

          <div className="row">
            <button
              type="submit"
              className="btn btn--primary"
              disabled={adjust.isPending || reason.trim() === ''}
            >
              Save adjustment
            </button>
            <button type="button" className="btn btn--secondary" onClick={() => setAdjusting(null)}>
              Cancel
            </button>
          </div>

          {adjust.isError && (
            <p className="muted" role="alert">
              {(adjust.error as Error).message}
            </p>
          )}
        </form>
      )}

      <DataTable
        caption="Stock levels, most constrained first"
        rows={query.data}
        rowKey={(row) => row.sku}
        emptyMessage="No stock records."
        columns={columns}
      />
    </div>
  );
}

// ---------------------------------------------------------------------------------------------
//  Users
// ---------------------------------------------------------------------------------------------

export function UsersPage() {
  const api = useApi();
  const queryClient = useQueryClient();
  const { can, user: currentUser } = useCurrentUser();
  const [search, setSearch] = useState('');

  const query = useQuery({
    queryKey: ['users', search],
    queryFn: () => api.searchUsers(search || undefined),
  });

  const setEnabled = useMutation({
    mutationFn: ({ userId, enabled }: { userId: string; enabled: boolean }) =>
      api.setUserEnabled(userId, enabled),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['users'] }),
  });

  const columns: Column<AdminUser>[] = [
    {
      header: 'Username',
      isRowHeader: true,
      render: (row) => <Link to={`/users/${row.id}`}>{row.username}</Link>,
    },
    { header: 'Email', render: (row) => row.email ?? '—' },
    {
      header: 'Status',
      render: (row) => (
        <span className={row.enabled ? 'badge badge--ok' : 'badge badge--low'}>
          {row.enabled ? 'Enabled' : 'Disabled'}
        </span>
      ),
    },
  ];

  if (can(Permissions.Users.Manage)) {
    columns.push({
      header: 'Actions',
      render: (row) => {
        // The server refuses this too. Hiding it here is a courtesy that saves a pointless round trip
        // and a confusing error; the server refusing it is what actually stops it.
        const isSelf = row.id === currentUser?.id;

        return (
          <button
            type="button"
            className="btn btn--secondary"
            disabled={setEnabled.isPending || (isSelf && row.enabled)}
            title={isSelf && row.enabled ? 'You cannot disable your own account' : undefined}
            onClick={() => setEnabled.mutate({ userId: row.id, enabled: !row.enabled })}
          >
            <span aria-hidden="true">{row.enabled ? 'Disable' : 'Enable'}</span>
            <span className="visually-hidden">
              {row.enabled ? 'Disable' : 'Enable'} {row.username}
            </span>
          </button>
        );
      },
    });
  }

  return (
    <div className="stack">
      <h1 className="page-title">Users</h1>

      <div className="card">
        <div className="field">
          <label htmlFor="user-search">Search</label>
          <input
            id="user-search"
            type="search"
            className="input"
            placeholder="Username or email"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
        </div>
      </div>

      {query.isPending ? (
        <Loading what="users" />
      ) : query.isError ? (
        <LoadError error={query.error} retry={() => query.refetch()} />
      ) : (
        <DataTable
          caption="Users in the realm"
          rows={query.data}
          rowKey={(row) => row.id}
          emptyMessage="No users match that search."
          columns={columns}
        />
      )}

      <p className="muted small">
        Users live in Keycloak, not in this application. Disabling an account here disables the login
        itself.
      </p>
    </div>
  );
}

export function UserDetailPage() {
  const { id } = useParams<{ id: string }>();
  const api = useApi();
  const queryClient = useQueryClient();
  const { can } = useCurrentUser();
  const [role, setRole] = useState<string>(ASSIGNABLE_ROLES[0]);

  const query = useQuery({ queryKey: ['user', id], queryFn: () => api.getUser(id!) });

  const changeRole = useMutation({
    mutationFn: ({ action, value }: { action: 'grant' | 'revoke'; value: string }) =>
      action === 'grant' ? api.assignRole(id!, value) : api.removeRole(id!, value),
    onSuccess: (updated) => {
      queryClient.setQueryData(['user', id], updated);
      void queryClient.invalidateQueries({ queryKey: ['users'] });
      void queryClient.invalidateQueries({ queryKey: ['audit'] });
    },
  });

  if (query.isPending) return <Loading what="the user" />;
  if (query.isError) return <LoadError error={query.error} retry={() => query.refetch()} />;

  const user = query.data;

  return (
    <div className="stack">
      <h1 className="page-title">{user.username}</h1>

      <section className="card stack" aria-labelledby="roles-heading">
        <h2 id="roles-heading">Roles</h2>

        <p className="muted small">
          A role is a job title. What it actually permits is a composite in Keycloak, so granting a
          permission to a role reaches everyone who holds it with no deployment.
        </p>

        <ul className="plain-list">
          {user.roles.length === 0 && <li className="muted">No roles assigned.</li>}

          {user.roles.map((assigned) => (
            <li key={assigned} className="row">
              <span>{assigned}</span>
              {can(Permissions.Users.ManageRoles) && (
                <button
                  type="button"
                  className="btn btn--secondary"
                  disabled={changeRole.isPending}
                  onClick={() => changeRole.mutate({ action: 'revoke', value: assigned })}
                >
                  <span aria-hidden="true">Remove</span>
                  <span className="visually-hidden">Remove role {assigned}</span>
                </button>
              )}
            </li>
          ))}
        </ul>

        {can(Permissions.Users.ManageRoles) && (
          <form
            className="row"
            onSubmit={(event) => {
              event.preventDefault();
              changeRole.mutate({ action: 'grant', value: role });
            }}
          >
            <div className="field">
              <label htmlFor="role">Grant a role</label>
              <select
                id="role"
                className="input"
                value={role}
                onChange={(event) => setRole(event.target.value)}
              >
                {ASSIGNABLE_ROLES.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            </div>

            <button type="submit" className="btn btn--primary" disabled={changeRole.isPending}>
              Grant
            </button>
          </form>
        )}

        {changeRole.isError && (
          <p className="muted" role="alert">
            {(changeRole.error as Error).message}
          </p>
        )}
      </section>
    </div>
  );
}

// ---------------------------------------------------------------------------------------------
//  Audit log
// ---------------------------------------------------------------------------------------------

export function AuditPage() {
  const api = useApi();

  const query = useQuery({ queryKey: ['audit'], queryFn: () => api.getAuditLog() });

  if (query.isPending) return <Loading what="the audit log" />;
  if (query.isError) return <LoadError error={query.error} retry={() => query.refetch()} />;

  const columns: Column<AuditEntry>[] = [
    { header: 'When', isRowHeader: true, render: (row) => formatDateTime(row.occurredAt) },
    { header: 'Who', render: (row) => row.actorName },
    { header: 'Did', render: (row) => row.action },
    { header: 'To', render: (row) => row.target },
    { header: 'Detail', render: (row) => row.detail ?? '—' },
  ];

  return (
    <div className="stack">
      <h1 className="page-title">Audit log</h1>

      <p className="muted small">
        Append-only. Entries record human decisions — an order the saga cancelled is not audited, an
        order a manager cancelled is.
      </p>

      <DataTable
        caption="Recent staff actions, newest first"
        rows={query.data}
        rowKey={(row) => `${row.occurredAt}-${row.action}-${row.target}`}
        emptyMessage="Nothing has been recorded yet."
        columns={columns}
      />
    </div>
  );
}

// ---------------------------------------------------------------------------------------------
//  Static pages
// ---------------------------------------------------------------------------------------------

export function ForbiddenPage() {
  return (
    <div className="centred">
      <div className="card stack" role="alert">
        <h1 className="page-title">You do not have access to this</h1>
        <p className="muted">
          Your account does not hold the permission this page needs. If you think it should, ask an
          administrator to check your roles.
        </p>
        <div>
          <Link className="btn btn--primary" to="/">
            Back to the dashboard
          </Link>
        </div>
      </div>
    </div>
  );
}

export function SignInPage() {
  const auth = useAuth();

  return (
    <div className="centred">
      <div className="card stack">
        <h1 className="page-title">Back office</h1>
        <p className="muted">Sign in with your staff account.</p>
        <div>
          <button type="button" className="btn btn--primary" onClick={() => void auth.signinRedirect()}>
            Sign in
          </button>
        </div>
      </div>
    </div>
  );
}

export function NotFoundPage() {
  return (
    <div className="centred">
      <div className="card stack">
        <h1 className="page-title">Page not found</h1>
        <div>
          <Link className="btn btn--primary" to="/">
            Back to the dashboard
          </Link>
        </div>
      </div>
    </div>
  );
}
