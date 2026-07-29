/**
 * Permission constants and the single `hasPermission` helper used by React,
 * Angular and React Native alike.
 *
 * These MIRROR `src/building-blocks/Auth/Permissions.cs` and the client roles in
 * `identity/keycloak/realm-export.json`. All three must agree; the authorization
 * tests are what stop them drifting.
 *
 * @see docs/authorization-model.md
 */

export const Permissions = {
  Catalog: {
    Read: 'catalog:read',
    Write: 'catalog:write',
    Delete: 'catalog:delete',
    PriceOverride: 'price:override',
  },
  Order: {
    /** Read ANY order. Staff only. */
    Read: 'order:read',
    /** Read only your own orders. Needs a server-side ownership check too. */
    ReadOwn: 'order:read:own',
    Write: 'order:write',
    Cancel: 'order:cancel',
    Refund: 'order:refund',
  },
  Inventory: {
    Read: 'inventory:read',
    Adjust: 'inventory:adjust',
  },
  Users: {
    Read: 'user:read',
    Manage: 'user:manage',
    ManageRoles: 'user:roles:manage',
  },
  Profile: {
    ReadOwn: 'profile:read:own',
    WriteOwn: 'profile:write:own',
  },
  Admin: {
    AuditRead: 'audit:read',
    DashboardRead: 'dashboard:read',
  },
} as const;

type Leaves<T> = T extends string ? T : { [K in keyof T]: Leaves<T[K]> }[keyof T];

/** Union of every valid permission string — a typo is a compile error. */
export type Permission = Leaves<typeof Permissions>;

export const Roles = {
  Customer: 'customer',
  SupportAgent: 'support-agent',
  CatalogManager: 'catalog-manager',
  OrderManager: 'order-manager',
  Admin: 'admin',
} as const;

export type Role = (typeof Roles)[keyof typeof Roles];

/** What the UI knows about the signed-in user. */
export interface AuthenticatedUser {
  /** Keycloak `sub` — the only stable identifier. */
  readonly id: string;
  readonly username: string;
  readonly email?: string;
  readonly displayName?: string;
  readonly roles: readonly string[];
  readonly permissions: readonly string[];
}

/**
 * Whether the user holds a permission.
 *
 * ---
 * **This is a user-experience helper, not a security control.**
 *
 * Hiding a button the user cannot use stops an honest person attempting
 * something that will fail. It stops a dishonest one from nothing at all —
 * anyone can open devtools, copy the token and call the API directly.
 *
 * Every permission checked here is *independently* enforced on the server, and
 * `tests/integration/ECommerce.Auth.IntegrationTests` proves it by calling
 * protected endpoints with lower-privileged tokens and requiring rejection.
 *
 * Never rely on this to protect anything.
 */
export function hasPermission(
  user: AuthenticatedUser | null | undefined,
  permission: Permission,
): boolean {
  return user?.permissions.includes(permission) ?? false;
}

/** Whether the user holds every one of the given permissions. */
export function hasAllPermissions(
  user: AuthenticatedUser | null | undefined,
  ...permissions: readonly Permission[]
): boolean {
  return permissions.every((permission) => hasPermission(user, permission));
}

/** Whether the user holds at least one of the given permissions. */
export function hasAnyPermission(
  user: AuthenticatedUser | null | undefined,
  ...permissions: readonly Permission[]
): boolean {
  return permissions.some((permission) => hasPermission(user, permission));
}

/**
 * Whether the user holds a realm role.
 *
 * Deliberately rare in application code. Prefer {@link hasPermission}: a role is
 * a job title, and gating UI on titles means every UI needs editing when the
 * business changes who may do what. Useful for display ("Signed in as
 * Administrator"), not for gating.
 */
export function hasRole(
  user: AuthenticatedUser | null | undefined,
  role: Role,
): boolean {
  return user?.roles.includes(role) ?? false;
}
