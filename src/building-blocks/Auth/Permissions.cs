namespace ECommerce.Auth;

/// <summary>
/// Every permission string in the system, as constants.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pattern:</b> Permission-based authorization. See <c>docs/authorization-model.md</c>.
/// </para>
/// <para>
/// <b>Why constants rather than raw strings at the call site.</b> A typo in
/// <c>RequirePermission("catalog:writ")</c> compiles, deploys, and then denies every request — or worse,
/// if the policy is registered by the same typo'd name, it silently matches nothing and the endpoint's
/// protection quietly evaporates. Authorization failures in the permissive direction are the ones nobody
/// notices. A constant turns that class of mistake into a build error.
/// </para>
/// <para>
/// These must stay in step with the client roles defined in
/// <c>identity/keycloak/realm-export.json</c>. <see cref="All"/> exists so a test can assert exactly that,
/// and that test is the thing preventing the two drifting apart.
/// </para>
/// <para>
/// <b>Naming convention: <c>resource:action</c>, optionally <c>:own</c>.</b> The <c>:own</c> suffix marks a
/// permission that cannot be decided from the token alone — <c>order:read:own</c> depends on <i>which</i>
/// order is being read, which is what resource-based authorization exists for. See
/// <see cref="ResourceOwnerRequirement"/>.
/// </para>
/// </remarks>
public static class Permissions
{
    /// <summary>The token claim carrying the caller's permissions.</summary>
    /// <remarks>
    /// Keycloak's default shape is <c>resource_access.ecommerce-api.roles</c>, which is awkward to read.
    /// A protocol mapper in the realm flattens it into a top-level <c>permissions</c> array instead, so the
    /// .NET side reads one simple string collection.
    /// </remarks>
    public const string ClaimType = "permissions";

    public static class Catalog
    {
        public const string Read = "catalog:read";
        public const string Write = "catalog:write";
        public const string Delete = "catalog:delete";
        public const string PriceOverride = "price:override";
    }

    public static class Order
    {
        /// <summary>Read <b>any</b> order. Staff only.</summary>
        public const string Read = "order:read";

        /// <summary>Read only orders you placed yourself. Requires a resource check — see remarks on this class.</summary>
        public const string ReadOwn = "order:read:own";

        public const string Write = "order:write";
        public const string Cancel = "order:cancel";
        public const string Refund = "order:refund";
    }

    public static class Inventory
    {
        public const string Read = "inventory:read";
        public const string Adjust = "inventory:adjust";
    }

    public static class Users
    {
        public const string Read = "user:read";
        public const string Manage = "user:manage";
        public const string ManageRoles = "user:roles:manage";
    }

    public static class Profile
    {
        public const string ReadOwn = "profile:read:own";
        public const string WriteOwn = "profile:write:own";
    }

    public static class Admin
    {
        public const string AuditRead = "audit:read";
        public const string DashboardRead = "dashboard:read";
    }

    /// <summary>
    /// Every permission, used to register one authorization policy per permission at startup, and to assert
    /// against the realm export in tests.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Catalog.Read, Catalog.Write, Catalog.Delete, Catalog.PriceOverride,
        Order.Read, Order.ReadOwn, Order.Write, Order.Cancel, Order.Refund,
        Inventory.Read, Inventory.Adjust,
        Users.Read, Users.Manage, Users.ManageRoles,
        Profile.ReadOwn, Profile.WriteOwn,
        Admin.AuditRead, Admin.DashboardRead,
    ];
}

/// <summary>
/// Realm role names. Present for completeness and for admin screens that display roles.
/// </summary>
/// <remarks>
/// <b>Deliberately not used to guard endpoints.</b> Endpoints require <see cref="Permissions"/>, never these.
/// A role is a job title; an endpoint cares about a capability. Guarding on the title means that when
/// "support agents can now issue refunds" arrives, you must find and edit every endpoint — and the ones you
/// miss fail silently in the permissive direction. Guarding on the capability means that change is a
/// composite-role edit in Keycloak and no deployment at all.
/// See <c>docs/authorization-model.md</c>.
/// </remarks>
public static class Roles
{
    public const string Customer = "customer";
    public const string SupportAgent = "support-agent";
    public const string CatalogManager = "catalog-manager";
    public const string OrderManager = "order-manager";
    public const string Admin = "admin";

    public static IReadOnlyList<string> All { get; } =
        [Customer, SupportAgent, CatalogManager, OrderManager, Admin];
}
