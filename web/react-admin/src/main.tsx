import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { AuthProvider } from 'react-oidc-context';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { oidcConfig } from './auth/oidc';
import { AdminLayout } from './components/AdminLayout';
import { RequirePermission } from './components/RequirePermission';
import { Permissions } from './lib/permissions';
import { CatalogPage, ProductEditPage } from './pages/CatalogPages';
import {
  AuditPage,
  DashboardPage,
  ForbiddenPage,
  InventoryPage,
  NotFoundPage,
  OrderDetailPage,
  OrdersPage,
  SignInPage,
  UserDetailPage,
  UsersPage,
} from './pages/AdminPages';
import { AuthCallbackPage, SilentRenewPage } from './pages/StaticPages';

import './styles/app.css';

/**
 * Composition root for the React back office.
 *
 * ---
 * **Every route declares the permission it needs**, right next to the component. That means the whole
 * authorization surface of the application is readable in one screen — and an unguarded route shows up
 * as an *absence*, which is far easier to spot in review than a missing check inside a component.
 *
 * It is the same principle as `RequirePermission(...)` on a minimal-API route, applied to the client.
 * And like that, **it is not the enforcement**: the Admin BFF checks the permission at the edge and
 * every service checks it again. See `RequirePermission.tsx`.
 */
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Shorter than the storefront's 30s. Admin data is operational - a stock level or a stuck saga
      // count that is half a minute stale is actively misleading to somebody acting on it.
      staleTime: 10_000,
      refetchOnWindowFocus: true,
      retry: 1,
    },
  },
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AuthProvider {...oidcConfig}>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          <Routes>
            {/* Outside the layout: it renders in a hidden iframe and must not draw a header. */}
            <Route path="/auth/silent-renew" element={<SilentRenewPage />} />

            <Route element={<AdminLayout />}>
              <Route
                index
                element={
                  <RequirePermission permission={Permissions.Admin.DashboardRead}>
                    <DashboardPage />
                  </RequirePermission>
                }
              />

              <Route
                path="catalog"
                element={
                  <RequirePermission permission={Permissions.Catalog.Read}>
                    <CatalogPage />
                  </RequirePermission>
                }
              />

              {/* One route for both add and edit - the id "new" means create. A separate /catalog/new
                  route would duplicate a 200-line form to change one boolean. */}
              <Route
                path="catalog/:id"
                element={
                  <RequirePermission permission={Permissions.Catalog.Write}>
                    <ProductEditPage />
                  </RequirePermission>
                }
              />

              <Route
                path="orders"
                element={
                  <RequirePermission permission={Permissions.Order.Read}>
                    <OrdersPage />
                  </RequirePermission>
                }
              />

              <Route
                path="orders/:id"
                element={
                  <RequirePermission permission={Permissions.Order.Read}>
                    <OrderDetailPage />
                  </RequirePermission>
                }
              />

              <Route
                path="inventory"
                element={
                  <RequirePermission permission={Permissions.Inventory.Read}>
                    <InventoryPage />
                  </RequirePermission>
                }
              />

              <Route
                path="users"
                element={
                  <RequirePermission permission={Permissions.Users.Read}>
                    <UsersPage />
                  </RequirePermission>
                }
              />

              <Route
                path="users/:id"
                element={
                  <RequirePermission permission={Permissions.Users.Read}>
                    <UserDetailPage />
                  </RequirePermission>
                }
              />

              <Route
                path="audit"
                element={
                  <RequirePermission permission={Permissions.Admin.AuditRead}>
                    <AuditPage />
                  </RequirePermission>
                }
              />

              {/* Deliberately NOT guarded - it is the page you land on when a guard sends you away. */}
              <Route path="forbidden" element={<ForbiddenPage />} />
              <Route path="signin" element={<SignInPage />} />
              <Route path="auth/callback" element={<AuthCallbackPage />} />
              <Route path="*" element={<NotFoundPage />} />
            </Route>
          </Routes>
        </BrowserRouter>
      </QueryClientProvider>
    </AuthProvider>
  </StrictMode>,
);
