import { expect, test, type Page } from '@playwright/test';

/**
 * The checkout saga, seen from the customer's side.
 *
 * Written ONCE and run against both storefronts.
 *
 * These are the slowest specs in the suite, and unavoidably so: they wait for real messages to travel
 * through RabbitMQ and be handled by three services. That is the behaviour under test — an order that
 * advanced instantly would mean the saga was not involved.
 */

const PASSWORD = 'Passw0rd!';

/** Long, because the saga crosses four services and a broker. Generous beats flaky. */
const SAGA_TIMEOUT = 40_000;

async function signIn(page: Page, username: string) {
  await page.goto('/');
  await page.getByRole('banner').getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL(/\/realms\/ecommerce\/protocol\/openid-connect\/auth/);
  await page.getByRole('textbox', { name: /username|email/i }).fill(username);
  await page.getByRole('textbox', { name: 'Password' }).fill(PASSWORD);
  await page.getByRole('button', { name: /^(sign in|log in)$/i }).click();
  await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible({ timeout: 20_000 });
}

async function emptyBasket(page: Page) {
  await page.goto('/basket');

  const empty = page.getByText('Your basket is empty.');
  const emptyButton = page.getByRole('button', { name: 'Empty basket' });

  await expect(empty.or(emptyButton).first()).toBeVisible({ timeout: 15_000 });

  if (await emptyButton.isVisible().catch(() => false)) {
    await emptyButton.click();
    await expect(empty).toBeVisible();
  }
}

/**
 * Chooses a size, if the product has sizes.
 *
 * A size is deliberately NOT pre-selected: the product page leaves "Add to basket" disabled until one
 * is picked, because defaulting a size means somebody buys a Small because it happened to be first.
 * (A colour IS pre-selected — the photograph already shows one.) So every spec that buys clothing has
 * to make the choice a customer would.
 *
 * Picks the first size that is not sold out. Sold-out sizes render disabled, and clicking a disabled
 * radio would hang until the timeout rather than fail with something readable.
 */
async function chooseAnAvailableSize(page: Page) {
  const group = page.getByRole('group', { name: 'Size' });

  // No size axis - a mug, a notebook. Nothing to choose.
  if ((await group.count()) === 0) return;

  const options = group.getByRole('radio');
  const total = await options.count();

  for (let index = 0; index < total; index++) {
    const option = options.nth(index);

    if (await option.isEnabled()) {
      // The LABEL is clicked, not the input. The radio is visually hidden and pointer-events:none -
      // it exists for the accessibility tree and for keyboard navigation - so the label is the
      // surface a person actually clicks, and clicking the input hangs until the timeout.
      //
      // click() then toBeChecked(), never check(): check() asserts once without retrying, and a
      // controlled input can be briefly out of step with the DOM mid-re-render.
      const name = ((await option.getAttribute('value')) ?? '').trim();
      await group.getByText(name, { exact: true }).click();
      await expect(option).toBeChecked();
      return;
    }
  }

  throw new Error('Every size is sold out; this product cannot be bought.');
}

/** Adds a named product and checks out. Returns the order reference. */
async function orderProduct(page: Page, search: string, recipient: string): Promise<string> {
  await emptyBasket(page);

  await page.goto(`/products?search=${encodeURIComponent(search)}`);
  await expect(page.getByRole('status')).toContainText('product');

  // Scoped to the product list by name. "The first h3 on the page" stopped meaning "the first
  // product" when the products page gained a category rail — see the note in shopping.spec.ts.
  await page
    .getByRole('list', { name: 'Products' })
    .getByRole('heading', { level: 3 })
    .first()
    .click();

  await chooseAnAvailableSize(page);
  await page.getByRole('button', { name: 'Add to basket' }).click();
  await expect(
    page.getByRole('status').filter({ hasText: 'Added to your basket' }),
  ).toBeVisible();

  await page.goto('/checkout');
  await page.getByLabel('Recipient').fill(recipient);
  await page.getByLabel('Address line 1').fill('12 Rosewood Avenue');
  await page.getByLabel('City').fill('Bristol');
  await page.getByLabel('Postcode').fill('BS1 4TP');
  await page.getByRole('button', { name: 'Place order' }).click();

  const heading = page.getByRole('heading', { level: 1 });
  await expect(heading).toContainText('Order ORD-', { timeout: 20_000 });

  return ((await heading.textContent()) ?? '').replace('Order ', '').trim();
}

// Serial for the same reason as the checkout specs: only three seed users hold `order:write`, and
// these tests each place an order and wait on shared inventory.
test.describe.configure({ mode: 'serial' });

test.describe('the checkout saga', () => {
  test('an order advances to paid on its own, without a refresh', async ({ page }) => {
    await signIn(page, 'customer');
    await orderProduct(page, 'Ceramic Mug', 'Casey Customer');

    // The customer sees "Order placed" first. Stock reservation and payment happen over the message
    // bus afterwards, and the page polls until it settles - so this transition arriving with no
    // interaction IS the feature.
    await expect(page.getByRole('status').filter({ hasText: 'Paid' }).first()).toBeVisible({
      timeout: SAGA_TIMEOUT,
    });
  });

  test('the order progress shows what the checkout process actually did', async ({ page }) => {
    await signIn(page, 'customer');
    await page.goto('/orders');
    await page.getByRole('link', { name: /^ORD-/ }).first().click();

    await expect(page.getByRole('heading', { name: 'Order progress', level: 2 })).toBeVisible({
      timeout: SAGA_TIMEOUT,
    });

    // Written in the customer's language, not the saga's. "CompensatingReleaseStock" is exactly right
    // in a log and meaningless on an order page.
    await expect(page.getByText('Order received')).toBeVisible();
    await expect(page.getByText('Stock reserved for you')).toBeVisible();
    await expect(page.getByText('Payment successful')).toBeVisible();
  });

  test('a payment failure cancels the order and releases the stock', async ({ page }) => {
    await signIn(page, 'ordermgr');

    // The Leather Portfolio costs 5,200 and the payment simulator declines anything at or above
    // 5,000. A deterministic threshold rather than a random failure rate, precisely so this test is
    // not a coin toss - see RequestPaymentHandler.DeclineThreshold.
    await orderProduct(page, 'Leather Portfolio', 'Olly Orders');

    // Placed successfully: the order exists, and the failure happens afterwards. That is the whole
    // point of an asynchronous saga, and it is what the customer actually experiences.
    await expect(page.getByRole('status').filter({ hasText: 'Cancelled' }).first()).toBeVisible({
      timeout: SAGA_TIMEOUT,
    });

    await expect(page.getByText('Payment was declined')).toBeVisible();
  });

  test('the compensation is visible and says nothing was charged', async ({ page }) => {
    await signIn(page, 'ordermgr');
    await page.goto('/orders');
    await page.getByRole('link', { name: /^ORD-/ }).first().click();

    await expect(page.getByRole('heading', { name: 'Order progress', level: 2 })).toBeVisible({
      timeout: SAGA_TIMEOUT,
    });

    // The step that makes the saga a saga: stock was reserved, then given back.
    await expect(page.getByText('Stock reserved for you')).toBeVisible();
    await expect(page.getByText('Payment declined')).toBeVisible();
    await expect(page.getByText('Releasing the reserved stock')).toBeVisible();

    // Said plainly rather than left to be inferred from a status word. A customer whose payment
    // failed needs to know their money is safe.
    await expect(
      page.getByText('Nothing was charged, and the items have been returned to stock.'),
    ).toBeVisible();
  });
});
