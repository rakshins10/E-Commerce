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
    loadComponent: () => import('./pages/static-pages').then((m) => m.ProductsPage),
    title: 'Products',
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
