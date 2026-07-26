import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { AuthProvider } from 'react-oidc-context';

import { oidcConfig } from './auth/oidc';
import { AppLayout } from './components/AppLayout';
import { HomePage } from './pages/HomePage';
import {
  AuthCallbackPage,
  ForbiddenPage,
  NotFoundPage,
  ProductsPage,
  SilentRenewPage,
} from './pages/StaticPages';

import './styles/app.css';

/**
 * Composition root for the React storefront.
 *
 * Provider order matters: AuthProvider wraps BrowserRouter so any route can
 * read auth state, and the silent-renew route sits OUTSIDE the layout because
 * it renders inside a hidden iframe and must not draw a header.
 */
createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AuthProvider {...oidcConfig}>
      <BrowserRouter>
        <Routes>
          {/* No layout - renders inside an invisible iframe. */}
          <Route path="/auth/silent-renew" element={<SilentRenewPage />} />

          <Route element={<AppLayout />}>
            <Route index element={<HomePage />} />
            <Route path="products" element={<ProductsPage />} />
            <Route path="auth/callback" element={<AuthCallbackPage />} />
            <Route path="forbidden" element={<ForbiddenPage />} />
            <Route path="*" element={<NotFoundPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  </StrictMode>,
);
