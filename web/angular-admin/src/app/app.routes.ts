import { Routes } from '@angular/router';

import { requirePermission } from './core/permission-guard';
import { Permissions } from './core/permissions';

/**
 * Back-office routes.
 *
 * ---
 * **Every route declares the permission it needs**, right next to the component. The whole
 * authorization surface of the application is readable in one screen — and an unguarded route shows up
 * as an *absence*, which is far easier to spot in review than a missing check inside a component.
 *
 * It is the same principle as `RequirePermission(...)` on a minimal-API route, applied to the client.
 * And like that, **it is not the enforcement**: the Admin BFF checks the permission at the edge and
 * every service checks it again. See `core/permission-guard.ts`.
 *
 * ---
 * **React/Angular divergence** (docs/react-vs-angular.md).
 *
 * Angular's `canActivate` runs **before** the component is created, so a forbidden route never mounts
 * and never fires its data request. React's `<RequirePermission>` renders, redirects, and only then
 * unmounts — harmless, but it means a guarded component's `useQuery` can briefly fire. A real point for
 * Angular's router.
 */
export const routes: Routes = [
  {
    path: '',
    canActivate: [requirePermission(Permissions.Admin.DashboardRead)],
    loadComponent: () => import('./pages/dashboard').then((m) => m.DashboardPage),
    title: 'Dashboard',
  },
  {
    path: 'catalog',
    canActivate: [requirePermission(Permissions.Catalog.Read)],
    loadComponent: () => import('./pages/catalog').then((m) => m.CatalogPage),
    title: 'Catalogue',
  },
  {
    // One route for both add and edit - the id "new" means create. A separate /catalog/new route
    // would duplicate a 200-line form to change one boolean.
    path: 'catalog/:id',
    canActivate: [requirePermission(Permissions.Catalog.Write)],
    loadComponent: () => import('./pages/catalog').then((m) => m.ProductEditPage),
    title: 'Product',
  },
  {
    path: 'orders',
    canActivate: [requirePermission(Permissions.Order.Read)],
    loadComponent: () => import('./pages/orders').then((m) => m.AdminOrdersPage),
    title: 'Orders',
  },
  {
    path: 'orders/:id',
    canActivate: [requirePermission(Permissions.Order.Read)],
    loadComponent: () => import('./pages/orders').then((m) => m.AdminOrderDetailPage),
    title: 'Order',
  },
  {
    path: 'inventory',
    canActivate: [requirePermission(Permissions.Inventory.Read)],
    loadComponent: () => import('./pages/inventory').then((m) => m.InventoryPage),
    title: 'Inventory',
  },
  {
    path: 'users',
    canActivate: [requirePermission(Permissions.Users.Read)],
    loadComponent: () => import('./pages/users').then((m) => m.UsersPage),
    title: 'Users',
  },
  {
    path: 'users/:id',
    canActivate: [requirePermission(Permissions.Users.Read)],
    loadComponent: () => import('./pages/users').then((m) => m.UserDetailPage),
    title: 'User',
  },
  {
    path: 'audit',
    canActivate: [requirePermission(Permissions.Admin.AuditRead)],
    loadComponent: () => import('./pages/static-pages').then((m) => m.AuditPage),
    title: 'Audit log',
  },

  // Deliberately UNGUARDED - it is the page a guard sends you to.
  {
    path: 'forbidden',
    loadComponent: () => import('./pages/static-pages').then((m) => m.ForbiddenPage),
    title: 'No access',
  },
  {
    path: 'signin',
    loadComponent: () => import('./pages/static-pages').then((m) => m.SignInPage),
    title: 'Sign in',
  },
  {
    path: 'auth/callback',
    loadComponent: () => import('./pages/static-pages').then((m) => m.AuthCallbackPage),
    title: 'Signing in',
  },
  {
    path: '**',
    loadComponent: () => import('./pages/static-pages').then((m) => m.NotFoundPage),
    title: 'Not found',
  },
];
