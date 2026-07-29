import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { AuthProvider } from 'react-oidc-context';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { oidcConfig } from './auth/oidc';
import { AppLayout } from './components/AppLayout';
import { HomePage } from './pages/HomePage';
import { ProductsPage } from './pages/ProductsPage';
import { ProductDetailPage } from './pages/ProductDetailPage';
import { AccountPage } from './pages/AccountPage';
import { BasketPage } from './pages/BasketPage';
import { CheckoutPage } from './pages/CheckoutPage';
import { OrderDetailPage, OrdersPage } from './pages/OrdersPage';
import { AuthCallbackPage, ForbiddenPage, NotFoundPage, SilentRenewPage } from './pages/StaticPages';

import './styles/app.css';

/**
 * Composition root for the React storefront.
 *
 * Provider order matters: AuthProvider wraps everything so any route can read
 * auth state, and the silent-renew route sits OUTSIDE the layout because it
 * renders inside a hidden iframe and must not draw a header.
 */
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // 30s: long enough that navigating back to a list does not refetch
      // immediately, short enough that a price change appears quickly.
      staleTime: 30_000,
      // Refetching every time the window regains focus is TanStack's default
      // and is usually noise on a storefront - it fires when the user alt-tabs
      // back from their email.
      refetchOnWindowFocus: false,
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
            {/* No layout - renders inside an invisible iframe. */}
            <Route path="/auth/silent-renew" element={<SilentRenewPage />} />

            <Route element={<AppLayout />}>
              <Route index element={<HomePage />} />
              <Route path="products" element={<ProductsPage />} />
              <Route path="products/:id" element={<ProductDetailPage />} />
              <Route path="basket" element={<BasketPage />} />
              <Route path="checkout" element={<CheckoutPage />} />
              <Route path="orders" element={<OrdersPage />} />
              <Route path="orders/:id" element={<OrderDetailPage />} />
              <Route path="account" element={<AccountPage />} />
              <Route path="auth/callback" element={<AuthCallbackPage />} />
              <Route path="forbidden" element={<ForbiddenPage />} />
              <Route path="*" element={<NotFoundPage />} />
            </Route>
          </Routes>
        </BrowserRouter>
      </QueryClientProvider>
    </AuthProvider>
  </StrictMode>,
);
