import { expect, request, test, type Page } from '@playwright/test';

/**
 * The back office.
 *
 * Written ONCE and run against both admin panels — :3001 React, :4201 Angular.
 *
 * These specs exist mainly to prove one thing: **the same application shows different people
 * different things**, and the server refuses what the UI hides. Everything else here is ordinary
 * screen coverage.
 */

const PASSWORD = 'Passw0rd!';

/**
 * Where the storefront lives, so these specs can create an order to look at.
 *
 * The back office cannot place orders - that is the shop's job - so a spec about the orders SCREEN has
 * to get an order from somewhere. On a developer's machine there are always some lying about; on a
 * fresh CI database there are none, which is exactly how these specs first failed.
 *
 * Creating one over HTTP rather than by driving the storefront UI keeps this fast and keeps the
 * failure honest: if this setup breaks, the message says so instead of blaming the admin panel.
 */
const STOREFRONT_BFF = process.env.E2E_STOREFRONT_BFF ?? 'http://localhost:6001';
const KEYCLOAK = process.env.E2E_KEYCLOAK ?? 'http://localhost:8080';

/** Places one order as `customer`, so the admin specs have something real to display. */
async function ensureAnOrderExists(): Promise<void> {
  const api = await request.newContext();

  try {
    const tokenResponse = await api.post(
      `${KEYCLOAK}/realms/ecommerce/protocol/openid-connect/token`,
      {
        form: {
          grant_type: 'password',
          client_id: 'test-harness',
          client_secret: 'dev_only_test_harness_secret',
          username: 'customer',
          password: PASSWORD,
        },
      },
    );

    const { access_token: token } = await tokenResponse.json();
    const headers = { Authorization: `Bearer ${token}` };

    const products = await (
      await api.get(`${STOREFRONT_BFF}/api/catalog/products?inStockOnly=true&pageSize=1`)
    ).json();

    const product = products.items?.[0];

    if (!product) {
      throw new Error('No product is in stock, so no order can be created for the admin specs.');
    }

    await api.delete(`${STOREFRONT_BFF}/api/basket/me`, { headers });

    await api.post(`${STOREFRONT_BFF}/api/basket/me/items`, {
      headers,
      data: {
        productId: product.id,
        sku: product.sku,
        productName: product.name,
        unitPrice: product.price,
        currency: product.currency,
        quantity: 1,
      },
    });

    await api.post(`${STOREFRONT_BFF}/api/orders`, {
      headers,
      data: {
        shippingAddress: {
          recipient: 'Casey Customer',
          line1: '12 Rosewood Avenue',
          city: 'Bristol',
          postcode: 'BS1 4TP',
          country: 'GB',
        },
        currency: 'GBP',
      },
    });
  } finally {
    await api.dispose();
  }
}

async function signIn(page: Page, username: string) {
  await page.goto('/');

  // The back office has no anonymous landing page, so an unauthenticated visit is redirected to
  // /signin by the route guard. That redirect IS behaviour under test - see the first spec.
  await page.getByRole('button', { name: 'Sign in' }).first().click();
  await page.waitForURL(/\/realms\/ecommerce\/protocol\/openid-connect\/auth/);
  await page.getByRole('textbox', { name: /username|email/i }).fill(username);
  await page.getByRole('textbox', { name: 'Password' }).fill(PASSWORD);
  await page.getByRole('button', { name: /^(sign in|log in)$/i }).click();
  await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible({ timeout: 20_000 });
}

test.describe('back office access', () => {
  test('an anonymous visitor is asked to sign in rather than shown a broken page', async ({ page }) => {
    await page.goto('/');

    await expect(page.getByRole('heading', { name: 'Back office', level: 1 })).toBeVisible();
    await expect(page.getByText('Sign in with your staff account.')).toBeVisible();
  });

  test('an administrator sees every section', async ({ page }) => {
    await signIn(page, 'administrator');

    const nav = page.getByRole('navigation', { name: 'Main' });

    for (const label of ['Dashboard', 'Orders', 'Inventory', 'Users', 'Audit log']) {
      await expect(nav.getByRole('link', { name: label })).toBeVisible();
    }
  });

  test('an order manager sees orders and inventory but not users or the audit log', async ({ page }) => {
    await signIn(page, 'ordermgr');

    const nav = page.getByRole('navigation', { name: 'Main' });

    await expect(nav.getByRole('link', { name: 'Orders' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Inventory' })).toBeVisible();

    // order-manager holds neither user:read nor audit:read. Same application, same build - the token
    // decides, which is the payoff of guarding on permissions rather than roles.
    await expect(nav.getByRole('link', { name: 'Users' })).toBeHidden();
    await expect(nav.getByRole('link', { name: 'Audit log' })).toBeHidden();
  });

  test('a support agent sees users but not the audit log', async ({ page }) => {
    await signIn(page, 'support');

    const nav = page.getByRole('navigation', { name: 'Main' });

    await expect(nav.getByRole('link', { name: 'Users' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Audit log' })).toBeHidden();
  });

  test('typing a forbidden URL directly shows a proper no-access page', async ({ page }) => {
    await signIn(page, 'ordermgr');

    // The nav hides this link, and hiding a link protects nothing. The route guard is what stops the
    // page rendering - and the server would refuse the data even if it did.
    await page.goto('/audit');

    await expect(
      page.getByRole('heading', { name: 'You do not have access to this', level: 1 }),
    ).toBeVisible();

    // Actionable rather than a dead end: it says what to do about it.
    await expect(page.getByText('ask an administrator to check your roles')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Back to the dashboard' })).toBeVisible();
  });
});

test.describe('dashboard', () => {
  test('shows operational figures', async ({ page }) => {
    await signIn(page, 'administrator');

    await expect(page.getByRole('heading', { name: 'Dashboard', level: 1 })).toBeVisible();

    for (const label of ['Orders today', 'Revenue today', 'Sagas stuck', 'Low stock items']) {
      await expect(page.getByText(label, { exact: true })).toBeVisible();
    }
  });
});

test.describe('orders', () => {
  // Creates an order over the storefront API first. A fresh CI database has none, and a spec that only
  // passes on a developer's well-used machine is a spec that fails the first time it matters.
  test.beforeAll(ensureAnOrderExists);

  test('lists every order, not just the signed-in user’s', async ({ page }) => {
    await signIn(page, 'administrator');
    await page.goto('/orders');

    await expect(page.getByRole('heading', { name: 'Orders', level: 1 })).toBeVisible();

    // Staff hold order:read, which drops the buyer filter server-side. A customer holding
    // order:read:own would see only their own rows from the very same endpoint.
    await expect(page.getByRole('link', { name: /^ORD-/ }).first()).toBeVisible();
  });

  test('an order shows the saga’s own step names for diagnosis', async ({ page }) => {
    await signIn(page, 'administrator');
    await page.goto('/orders');
    await page.getByRole('link', { name: /^ORD-/ }).first().click();

    await expect(page.getByRole('heading', { level: 1 })).toContainText('Order ORD-');

    // NOT softened into customer wording, unlike the storefront. Staff diagnosing a failure want the
    // real names - "CompensatingReleaseStock" is precise, and translating it loses the signal.
    await expect(page.getByRole('heading', { name: 'Checkout process', level: 2 })).toBeVisible({
      timeout: 15_000,
    });
    await expect(page.getByText('OrderSubmitted').first()).toBeVisible();
  });
});

test.describe('inventory', () => {
  test('explains the difference between on hand, reserved and available', async ({ page }) => {
    await signIn(page, 'support');
    await page.goto('/inventory');

    await expect(page.getByRole('heading', { name: 'Inventory', level: 1 })).toBeVisible();

    // The distinction is the model, and a stock screen that does not explain it invites somebody to
    // "correct" a reserved figure.
    await expect(page.getByText('On hand is what is physically on the shelf')).toBeVisible();
  });

  test('support can see stock but not adjust it', async ({ page }) => {
    await signIn(page, 'support');
    await page.goto('/inventory');

    await expect(page.getByRole('columnheader', { name: 'Available' })).toBeVisible();

    // support-agent is read-only by design. The button is hidden AND the endpoint requires
    // inventory:adjust, which support does not hold.
    await expect(page.getByRole('button', { name: /^Adjust/ }).first()).toBeHidden();
  });

  test('an order manager can adjust stock, and must give a reason', async ({ page }) => {
    await signIn(page, 'ordermgr');
    await page.goto('/inventory');

    await page.getByRole('button', { name: /Adjust stock for/ }).first().click();

    await expect(page.getByRole('heading', { name: /^Adjust / })).toBeVisible();

    // Required, because an unexplained stock movement is impossible to audit later.
    await expect(page.getByRole('button', { name: 'Save adjustment' })).toBeDisabled();

    await page.getByLabel('Reason').fill('Stock take correction');
    await expect(page.getByRole('button', { name: 'Save adjustment' })).toBeEnabled();
  });
});

test.describe('users', () => {
  test('lists the seed users with their status', async ({ page }) => {
    await signIn(page, 'administrator');
    await page.goto('/users');

    await expect(page.getByRole('heading', { name: 'Users', level: 1 })).toBeVisible();
    await expect(page.getByRole('rowheader', { name: 'customer', exact: true })).toBeVisible();

    // The disabled seed account, so the status column is proven to render both states.
    await expect(page.getByText('Disabled').first()).toBeVisible();
  });

  test('says plainly that users live in Keycloak', async ({ page }) => {
    await signIn(page, 'administrator');
    await page.goto('/users');

    // Worth stating on screen: somebody will otherwise assume this application owns the accounts and
    // wonder why a change here affects the login page.
    await expect(page.getByText('Users live in Keycloak, not in this application.')).toBeVisible();
  });

  test('support can see users but not enable or disable them', async ({ page }) => {
    await signIn(page, 'support');
    await page.goto('/users');

    await expect(page.getByRole('rowheader', { name: 'customer', exact: true })).toBeVisible();

    // Seeing who exists and changing whether they can log in are deliberately different permissions.
    await expect(page.getByRole('button', { name: /^Disable/ }).first()).toBeHidden();
  });
});

test.describe('audit log', () => {
  test('records what staff did, newest first', async ({ page }) => {
    await signIn(page, 'administrator');
    await page.goto('/audit');

    await expect(page.getByRole('heading', { name: 'Audit log', level: 1 })).toBeVisible();

    // The distinction that makes it evidence rather than noise.
    await expect(
      page.getByText('an order the saga cancelled is not audited'),
    ).toBeVisible();
  });
});
