import { Routes } from '@angular/router';

/**
 * Storefront routes.
 *
 * Mirrors the React storefront's route table exactly, so the shared Playwright
 * specs can navigate both apps with the same paths.
 *
 * Every page is lazily loaded with `loadComponent`, so Angular emits a separate
 * chunk per route and the initial download contains only the shell. React
 * achieves the same with `React.lazy` — Angular makes it the default shape of a
 * route definition rather than something you remember to add.
 */
export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./pages/home').then((m) => m.HomePage),
    title: 'Storefront',
  },
  {
    path: 'products',
    loadComponent: () => import('./pages/products').then((m) => m.ProductsPage),
    title: 'Products',
  },
  {
    path: 'products/:id',
    loadComponent: () => import('./pages/product-detail').then((m) => m.ProductDetailPage),
    title: 'Product',
  },
  {
    path: 'basket',
    loadComponent: () => import('./pages/basket').then((m) => m.BasketPage),
    title: 'Your basket',
  },
  {
    path: 'checkout',
    loadComponent: () => import('./pages/checkout').then((m) => m.CheckoutPage),
    title: 'Checkout',
  },
  {
    path: 'orders',
    loadComponent: () => import('./pages/orders').then((m) => m.OrdersPage),
    title: 'Your orders',
  },
  {
    path: 'orders/:id',
    loadComponent: () => import('./pages/orders').then((m) => m.OrderDetailPage),
    title: 'Order',
  },
  {
    path: 'account',
    loadComponent: () => import('./pages/account').then((m) => m.AccountPage),
    title: 'My account',
  },
  {
    path: 'auth/callback',
    loadComponent: () => import('./pages/static-pages').then((m) => m.AuthCallbackPage),
    title: 'Signing in',
  },
  {
    // Target of the hidden silent-renew iframe. Renders nothing.
    path: 'auth/silent-renew',
    loadComponent: () => import('./pages/static-pages').then((m) => m.SilentRenewPage),
  },
  {
    path: 'forbidden',
    loadComponent: () => import('./pages/static-pages').then((m) => m.ForbiddenPage),
    title: 'Not permitted',
  },
  {
    path: '**',
    loadComponent: () => import('./pages/static-pages').then((m) => m.NotFoundPage),
    title: 'Page not found',
  },
];
