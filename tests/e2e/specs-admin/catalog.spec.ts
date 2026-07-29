import { expect, request, test, type Page } from '@playwright/test';

/**
 * Catalogue management.
 *
 * Written ONCE and run against both admin panels — :3001 React, :4201 Angular.
 *
 * These specs create real products, so each uses a **unique SKU derived from the framework under
 * test**. Two apps running the same spec against one shared database would otherwise collide on the
 * second run, and the failure would look like a bug in whichever ran second.
 *
 * ---
 * **They also withdraw everything they create.** The storefront specs assert on the exact seeded
 * catalogue — "12 products", "3 products match hoodie" — so a product left behind by this suite breaks
 * nine assertions in another one. That happened on the first run, and the failures pointed at the
 * storefront rather than at the suite that had actually caused them.
 *
 * The cleanup uses the withdraw endpoint, which is the feature under test: a withdrawn product is
 * invisible to the storefront while its row survives, so the counts go back to what they were without
 * deleting anything.
 */

const PASSWORD = 'Passw0rd!';
const ADMIN_BFF = process.env.E2E_ADMIN_BFF ?? 'http://localhost:6002';
const KEYCLOAK = process.env.E2E_KEYCLOAK ?? 'http://localhost:8080';

/**
 * A SKU nothing else will claim.
 *
 * Includes the target so React and Angular never collide, and a timestamp so a second run does not
 * trip the uniqueness rule the first run's product now holds. `E2E-` prefixed so anything left behind
 * is obvious in the database.
 */
function uniqueSku(): string {
  const target = process.env.E2E_TARGET ?? 'react';
  return `E2E-${target.toUpperCase()}-${Date.now().toString().slice(-8)}`;
}

async function signIn(page: Page, username: string) {
  await page.goto('/');
  await page.getByRole('button', { name: 'Sign in' }).first().click();
  await page.waitForURL(/\/realms\/ecommerce\/protocol\/openid-connect\/auth/);
  await page.getByRole('textbox', { name: /username|email/i }).fill(username);
  await page.getByRole('textbox', { name: 'Password' }).fill(PASSWORD);
  await page.getByRole('button', { name: /^(sign in|log in)$/i }).click();
  await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible({ timeout: 20_000 });
}

/**
 * Creates a product through the UI.
 *
 * Returns BOTH the SKU and the name, and the name is unique per run for the same reason the SKU is:
 * cleanup withdraws rather than deletes, so yesterday's "Withdrawal Widget" is still in the withdrawn
 * list today. A button whose accessible name is "Put Withdrawal Widget back on sale" then matches three
 * elements and Playwright refuses to guess.
 */
async function createProduct(page: Page, label: string): Promise<{ sku: string; name: string }> {
  const sku = uniqueSku();
  const name = `${label} ${sku}`;

  await page.goto('/catalog/new');

  await page.getByLabel('SKU').fill(sku);
  await page.getByLabel('Name').fill(name);
  await page.getByLabel('Description').fill('Created by the shared catalogue spec.');
  await page.getByLabel('Price').fill('12.34');

  await page.getByRole('button', { name: 'Create product' }).click();

  // Redirects to the edit page on success, which is how we know it saved.
  await expect(page.getByRole('status')).toContainText('Product saved', { timeout: 20_000 });

  // Recorded BEFORE any assertion that could fail, so a failing spec still gets cleaned up.
  created.push(sku);

  return { sku, name };
}

/** Everything this suite creates, so it can clean up after itself. */
const created: string[] = [];

// Serial: these specs create and withdraw products in a shared database, and a search assertion in
// one would otherwise race a create in another.
test.describe.configure({ mode: 'serial' });

/**
 * Withdraws every product this suite created.
 *
 * Over the API rather than through the UI: cleanup that drives a browser is slow, and cleanup that
 * fails should say "cleanup failed" rather than leaving a half-finished journey to be diagnosed.
 */
test.afterAll(async () => {
  if (created.length === 0) return;

  const api = await request.newContext();

  try {
    const token = await (
      await api.post(`${KEYCLOAK}/realms/ecommerce/protocol/openid-connect/token`, {
        form: {
          grant_type: 'password',
          client_id: 'test-harness',
          client_secret: 'dev_only_test_harness_secret',
          username: 'catalogmgr',
          password: PASSWORD,
        },
      })
    ).json();

    const headers = { Authorization: `Bearer ${token.access_token}` };

    for (const sku of created) {
      const found = await (
        await api.get(`${ADMIN_BFF}/api/catalog/products?search=${sku}&pageSize=5`, { headers })
      ).json();

      const product = found.items?.find((p: { sku: string }) => p.sku === sku);

      if (product) {
        await api.delete(`${ADMIN_BFF}/api/catalog/products/${product.id}`, { headers });
      }
    }
  } finally {
    await api.dispose();
  }
});

test.describe('catalogue', () => {
  test('a catalogue manager sees the catalogue in the navigation', async ({ page }) => {
    await signIn(page, 'catalogmgr');

    await expect(
      page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Catalogue' }),
    ).toBeVisible();
  });

  test('the product list loads with the seeded catalogue', async ({ page }) => {
    await signIn(page, 'catalogmgr');
    await page.goto('/catalog');

    await expect(page.getByRole('heading', { name: 'Catalogue', level: 1 })).toBeVisible();
    await expect(page.getByRole('rowheader', { name: 'NW-TS-001' })).toBeVisible();
  });

  test('a product can be created', async ({ page }) => {
    await signIn(page, 'catalogmgr');

    const { sku } = await createProduct(page, 'Spec Widget');

    await page.goto('/catalog');
    await page.getByLabel('Search').fill(sku);

    await expect(page.getByRole('rowheader', { name: sku })).toBeVisible({ timeout: 15_000 });
  });

  test('a duplicate SKU is refused with a message that names the SKU', async ({ page }) => {
    await signIn(page, 'catalogmgr');

    // NW-TS-001 is seeded, so this collides deterministically rather than depending on what an
    // earlier spec happened to leave behind.
    await page.goto('/catalog/new');
    await page.getByLabel('SKU').fill('NW-TS-001');
    await page.getByLabel('Name').fill('Duplicate attempt');
    await page.getByRole('button', { name: 'Create product' }).click();

    // The server's own message, surfaced verbatim. A generic "something went wrong" would leave a
    // merchandiser with no idea which field to change.
    await expect(page.getByRole('alert')).toContainText("already in use", { timeout: 15_000 });
  });

  test('a SKU cannot be changed once the product exists', async ({ page }) => {
    await signIn(page, 'catalogmgr');
    await page.goto('/catalog');

    await page.getByRole('link', { name: 'NW-TS-001' }).click();

    // Historic order lines reference the SKU, so renaming one silently decouples an order from what
    // was shipped. Disabled in the UI; absent from the update request entirely.
    await expect(page.getByLabel('SKU')).toBeDisabled();
    await expect(page.getByText('A SKU cannot be changed')).toBeVisible();
  });

  test('a product can be edited', async ({ page }) => {
    await signIn(page, 'catalogmgr');
    await page.goto('/catalog');
    await page.getByRole('link', { name: 'NW-TS-001' }).click();

    await page.getByLabel('Description').fill('Edited by the shared catalogue spec.');
    await page.getByRole('button', { name: 'Save changes' }).click();

    await expect(page.getByRole('status')).toContainText('Product saved', { timeout: 15_000 });
  });

  test('changing a price is a separate action with its own permission', async ({ page }) => {
    await signIn(page, 'catalogmgr');
    await page.goto('/catalog');
    await page.getByRole('link', { name: 'NW-TS-001' }).click();

    // Said on screen, because "why is there a second form for one number?" is the obvious question.
    await expect(page.getByText('price:override')).toBeVisible();

    // Read the current price, change it, then put it back. The storefront specs assert this
    // product's price to the penny, so a spec that leaves it changed breaks another suite - which is
    // exactly what happened the first time this ran.
    const priceField = page.getByLabel('New price');
    const original = await priceField.inputValue();

    await priceField.fill('99.99');
    await page.getByRole('button', { name: 'Change price' }).click();
    await expect(page.getByRole('status')).toContainText('Price changed to', { timeout: 15_000 });

    await priceField.fill(original);
    await page.getByRole('button', { name: 'Change price' }).click();
    await expect(page.getByRole('status')).toContainText('Price changed to', { timeout: 15_000 });
  });

  test('a withdrawn product leaves the list and can be restored', async ({ page }) => {
    await signIn(page, 'catalogmgr');

    const { sku, name } = await createProduct(page, 'Withdrawal Widget');

    await page.goto('/catalog');
    await page.getByLabel('Search').fill(sku);
    await expect(page.getByRole('rowheader', { name: sku })).toBeVisible({ timeout: 15_000 });

    await page.getByRole('button', { name: `Withdraw ${name} from sale` }).click();

    // Gone from the on-sale list...
    await expect(page.getByRole('rowheader', { name: sku })).toBeHidden({ timeout: 15_000 });

    // ...but findable, because otherwise the only way back would be the database.
    await page.getByLabel('Show withdrawn products').click();
    await expect(page.getByRole('rowheader', { name: sku })).toBeVisible({ timeout: 15_000 });

    await page.getByRole('button', { name: `Put ${name} back on sale` }).click();
    await expect(page.getByRole('rowheader', { name: sku })).toBeHidden({ timeout: 15_000 });
  });

  test('support can see the catalogue but not change it', async ({ page }) => {
    await signIn(page, 'support');
    await page.goto('/catalog');

    await expect(page.getByRole('heading', { name: 'Catalogue', level: 1 })).toBeVisible();

    // support-agent holds catalog:read and nothing else on the catalogue. Both are hidden here AND
    // refused by the gateway and the service - the UI is the courtesy, not the control.
    await expect(page.getByRole('link', { name: 'Add product' })).toBeHidden();
    await expect(page.getByRole('button', { name: /^Withdraw/ }).first()).toBeHidden();
  });

  test('an order manager cannot reach the catalogue editor at all', async ({ page }) => {
    await signIn(page, 'ordermgr');

    // order-manager holds catalog:read, so the list is reachable - but not catalog:write, so the
    // editor is not. Typing the URL directly gets the no-access page.
    await page.goto('/catalog/new');

    await expect(
      page.getByRole('heading', { name: 'You do not have access to this', level: 1 }),
    ).toBeVisible();
  });
});
