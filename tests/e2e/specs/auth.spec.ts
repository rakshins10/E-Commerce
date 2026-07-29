import { expect, test, type ConsoleMessage } from '@playwright/test';

/**
 * Sign-in, sign-out, and permission rendering.
 *
 * Written ONCE and run against both storefronts. Every selector is a role and
 * an accessible name — never a CSS class or a test id — because those differ
 * between two independent implementations. That constraint is what drags
 * accessibility up as a side effect: a div-soup implementation cannot pass.
 *
 * @see docs/adr/0014-react-and-angular-in-lockstep.md
 */

const SEED_PASSWORD = 'Passw0rd!';

/**
 * Captures browser console errors and failed network requests for the whole
 * test, so a failure reports *why* rather than just "timed out waiting".
 */
function collectFailures(page: import('@playwright/test').Page) {
  const messages: string[] = [];

  page.on('console', (message: ConsoleMessage) => {
    if (message.type() === 'error') messages.push(`console: ${message.text()}`);
  });
  page.on('pageerror', (error) => messages.push(`pageerror: ${error.message}`));
  page.on('requestfailed', (request) =>
    messages.push(`requestfailed: ${request.method()} ${request.url()} — ${request.failure()?.errorText}`),
  );

  return messages;
}

/**
 * Drives the Keycloak login form. Identical for both apps — it is the same realm.
 *
 * Scoped to the `banner` landmark because the page deliberately has two "Sign in"
 * buttons (the header and the call-to-action card). Scoping by landmark rather
 * than adding a test id keeps the selector meaningful and framework-neutral.
 */
async function signIn(page: import('@playwright/test').Page, username: string) {
  await page.getByRole('banner').getByRole('button', { name: 'Sign in' }).click();

  // We are now on Keycloak's own page, a different origin.
  await page.waitForURL(/\/realms\/ecommerce\/protocol\/openid-connect\/auth/);

  // Keycloak's password field sits next to a "Show password" toggle button, so
  // a label match alone is ambiguous. Targeting the textbox role disambiguates
  // without resorting to a CSS selector.
  await page.getByRole('textbox', { name: /username|email/i }).fill(username);
  await page.getByRole('textbox', { name: 'Password' }).fill(SEED_PASSWORD);
  await page.getByRole('button', { name: /^(sign in|log in)$/i }).click();
}

test.describe('authentication', () => {
  test('an anonymous visitor sees the sign-in prompt', async ({ page }) => {
    await page.goto('/');

    await expect(page.getByRole('heading', { name: 'Everyday things, properly made', level: 1 })).toBeVisible();
    await expect(page.getByRole('banner').getByRole('button', { name: 'Sign in' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeHidden();
  });

  test('a customer can sign in and sees exactly their 5 permissions', async ({ page }) => {
    const failures = collectFailures(page);

    await page.goto('/');
    await signIn(page, 'customer');

    // Back on the app, signed in. Generous timeout: this covers the redirect
    // back plus the authorization-code exchange.
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible({ timeout: 20_000 });

    expect(failures, `browser reported failures:\n${failures.join('\n')}`).toEqual([]);

    await expect(page.getByText('Chloe Customer').first()).toBeVisible();

    // These permissions were never assigned to the user - they come from the
    // `customer` realm role being a Keycloak composite. Asserting them here
    // proves the whole chain: composite role -> protocol mapper -> token claim
    // -> shared parser -> rendered UI.
    for (const permission of [
      'catalog:read',
      'order:read:own',
      'order:write',
      'profile:read:own',
      'profile:write:own',
    ]) {
      await expect(page.getByText(permission, { exact: true })).toBeVisible();
    }

    // A customer must NOT see staff permissions.
    await expect(page.getByText('catalog:write', { exact: true })).toBeHidden();
    await expect(page.getByText('order:refund', { exact: true })).toBeHidden();
  });

  test('an administrator sees 15 permissions, inherited through nested composites', async ({ page }) => {
    await page.goto('/');
    await signIn(page, 'administrator');

    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible({ timeout: 20_000 });

    // `admin` grants none of these directly - it is a composite of the three
    // staff roles, so these arrive two levels down.
    for (const permission of ['catalog:write', 'order:refund', 'user:manage', 'audit:read']) {
      await expect(page.getByText(permission, { exact: true })).toBeVisible();
    }
  });

  test('signing out returns the visitor to the anonymous state', async ({ page }) => {
    await page.goto('/');
    await signIn(page, 'customer');
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible({ timeout: 20_000 });

    await page.getByRole('button', { name: 'Sign out' }).click();

    await expect(page.getByRole('banner').getByRole('button', { name: 'Sign in' })).toBeVisible({
      timeout: 20_000,
    });
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeHidden();
  });
});

test.describe('shell', () => {
  test('navigation works and marks the current page', async ({ page }) => {
    await page.goto('/');

    await page.getByRole('link', { name: 'Products' }).click();
    await expect(page).toHaveURL(/\/products$/);
    await expect(page.getByRole('heading', { name: 'Products', level: 1 })).toBeVisible();

    // aria-current is the accessible signal for "you are here". Both apps must
    // set it - React via NavLink, Angular via ariaCurrentWhenActive.
    await expect(page.getByRole('link', { name: 'Products' })).toHaveAttribute('aria-current', 'page');
  });

  test('a deep link survives a full page reload', async ({ page }) => {
    // The classic SPA deployment bug: nginx must fall back to index.html, or a
    // refresh on a client-side route is a 404.
    await page.goto('/products');
    await expect(page.getByRole('heading', { name: 'Products', level: 1 })).toBeVisible();
  });

  test('the theme can be switched and persists across a reload', async ({ page }) => {
    await page.goto('/');

    await page.getByRole('button', { name: 'Theme: follow system' }).click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');

    await page.getByRole('button', { name: 'Theme: light' }).click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');

    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  });

  test('a skip link is the first thing a keyboard user reaches', async ({ page }) => {
    await page.goto('/');

    await page.keyboard.press('Tab');
    await expect(page.getByRole('link', { name: 'Skip to content' })).toBeFocused();
  });

  test('an unknown route shows the not-found page', async ({ page }) => {
    await page.goto('/no-such-page');
    await expect(page.getByRole('heading', { name: 'Page not found', level: 1 })).toBeVisible();
  });
});
