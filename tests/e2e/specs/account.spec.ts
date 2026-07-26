import { expect, test, type Page } from '@playwright/test';

/**
 * My Account — profile, addresses and preferences.
 *
 * Written ONCE and run against both storefronts.
 *
 * These specs mutate server state, so each signs in as a **different seed user**
 * where practical, and the address specs clean up after themselves. Sharing one
 * user across mutating tests makes them order-dependent, which is how an e2e
 * suite becomes flaky and then ignored.
 */

const PASSWORD = 'Passw0rd!';

async function signIn(page: Page, username: string) {
  await page.goto('/');
  await page.getByRole('banner').getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL(/\/realms\/ecommerce\/protocol\/openid-connect\/auth/);
  await page.getByRole('textbox', { name: /username|email/i }).fill(username);
  await page.getByRole('textbox', { name: 'Password' }).fill(PASSWORD);
  await page.getByRole('button', { name: /^(sign in|log in)$/i }).click();
  await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible({ timeout: 20_000 });
}

test.describe('my account', () => {
  test('is not reachable anonymously and prompts to sign in', async ({ page }) => {
    await page.goto('/account');

    await expect(page.getByRole('heading', { name: 'My account', level: 1 })).toBeVisible();
    await expect(
      page.getByText('Sign in to manage your profile, addresses and preferences.'),
    ).toBeVisible();
  });

  test('the account link only appears once signed in', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('link', { name: 'My account' })).toBeHidden();

    await signIn(page, 'customer');

    await expect(page.getByRole('link', { name: 'My account' })).toBeVisible();
  });

  test('the profile is provisioned on first visit and shows the three sections', async ({ page }) => {
    await signIn(page, 'ordermgr');
    await page.goto('/account');

    // The profile did not exist before this request - it is created lazily on
    // the first authenticated call, never during registration.
    await expect(page.getByRole('heading', { name: 'Contact details', level: 2 })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Addresses', level: 2 })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Preferences', level: 2 })).toBeVisible();

    await expect(page.getByText('You have no saved addresses yet.')).toBeVisible();
  });

  test('email is read-only because it is identity data, not profile data', async ({ page }) => {
    await signIn(page, 'support');
    await page.goto('/account');

    // Owned by Keycloak. Changing it means changing your login, which belongs in
    // account security rather than here. See docs/adr/0004.
    // `exact` matters: the preferences section has "Send me offers by email"
    // and "Email me about my orders", so a substring match finds three controls.
    await expect(page.getByLabel('Email', { exact: true })).toBeDisabled();
    await expect(page.getByLabel('Display name')).toBeEnabled();
  });

  test('contact details can be saved', async ({ page }) => {
    await signIn(page, 'catalogmgr');
    await page.goto('/account');

    await page.getByLabel('Display name').fill('Casey Catalogue');
    await page.getByRole('button', { name: 'Save contact details' }).click();

    await expect(page.getByRole('status')).toContainText('Contact details saved');

    await page.reload();
    await expect(page.getByLabel('Display name')).toHaveValue('Casey Catalogue');
  });

  test('the first address becomes the default for both shipping and billing', async ({ page }) => {
    await signIn(page, 'administrator');
    await page.goto('/account');

    // Clean slate - these specs mutate real state.
    while (await page.getByRole('button', { name: 'Remove' }).first().isVisible().catch(() => false)) {
      await page.getByRole('button', { name: 'Remove' }).first().click();
      await page.waitForTimeout(300);
    }

    await page.getByRole('button', { name: 'Add address' }).click();
    await page.getByLabel('Label').fill('Home');
    await page.getByLabel('Address line 1').fill('12 Rosewood Avenue');
    await page.getByLabel('City').fill('Bristol');
    await page.getByLabel('Postcode').fill('BS1 4TP');
    await page.getByRole('button', { name: 'Save address' }).click();

    await expect(page.getByRole('status')).toContainText('Address saved');

    // A customer with exactly one address and no default would be asked to
    // choose from a list of one at checkout, so the aggregate sets both.
    await expect(page.getByText('Default shipping')).toBeVisible();
    await expect(page.getByText('Default billing')).toBeVisible();
  });

  test('making a second address the default removes the flag from the first', async ({ page }) => {
    await signIn(page, 'administrator');
    await page.goto('/account');

    await page.getByRole('button', { name: 'Add address' }).click();
    await page.getByLabel('Label').fill('Work');
    await page.getByLabel('Address line 1').fill('400 Temple Quay');
    await page.getByLabel('City').fill('Bristol');
    await page.getByLabel('Postcode').fill('BS1 6EA');
    await page.getByRole('button', { name: 'Save address' }).click();
    await expect(page.getByRole('status')).toContainText('Address saved');

    // Promote Work to default shipping.
    await page.getByRole('button', { name: 'Use for shipping' }).first().click();

    // The invariant: exactly ONE default shipping address, enforced inside the
    // aggregate rather than by the client.
    await expect(page.getByText('Default shipping')).toHaveCount(1);
  });

  test('preferences can be changed and persist', async ({ page }) => {
    await signIn(page, 'customer');
    await page.goto('/account');

    await page.getByLabel('Currency').selectOption('EUR');
    await page.getByLabel('Send me offers by email').check();
    await page.getByRole('button', { name: 'Save preferences' }).click();

    await expect(page.getByRole('status')).toContainText('Preferences saved');

    await page.reload();
    await expect(page.getByLabel('Currency')).toHaveValue('EUR');
    await expect(page.getByLabel('Send me offers by email')).toBeChecked();
  });

  test('marketing and order updates are presented as separate groups', async ({ page }) => {
    await signIn(page, 'customer');
    await page.goto('/account');

    // Legally distinct: marketing needs opt-in and can be withdrawn, an order
    // confirmation is part of the contract. Presenting them together invites a
    // single "notifications" toggle, which is the mistake.
    await expect(page.getByRole('group', { name: 'Marketing' })).toBeVisible();
    await expect(page.getByRole('group', { name: 'Order updates' })).toBeVisible();
  });
});
