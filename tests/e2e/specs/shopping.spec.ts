import { expect, test, type Locator, type Page } from '@playwright/test';

/**
 * Basket, checkout and orders.
 *
 * Written ONCE and run against both storefronts.
 *
 * These specs place real orders, which cannot be deleted. They are written to be **repeatable**
 * regardless: each starts by emptying the basket, and none asserts on a total that depends on what a
 * previous run left behind. A suite that only passes on a clean database gets ignored within a week —
 * which is a lesson from the account specs in Phase 5, where exactly that happened.
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

/** Empties the basket so a spec starts from a known state. */
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

/** Adds the first product on the products page and returns its name. */
async function addFirstProduct(page: Page): Promise<string> {
  await page.goto('/products');
  await expect(page.getByRole('status')).toContainText('products');

  const firstProduct = page.getByRole('link', { name: /view|details/i }).first();

  // Scoped to the product list BY NAME, not to "the first h3 on the page".
  //
  // It was the latter, and it worked right up until the products page grew a category rail whose
  // department names are also h3 — at which point "the first product" became "Accessories", the
  // click navigated to a filtered list, and the failure surfaced as a missing "Add to basket"
  // button two assertions later. An unanchored positional selector is a spec that quietly depends on
  // document order.
  const productLinks = page
    .getByRole('list', { name: 'Products' })
    .getByRole('heading', { level: 3 });

  // The card layout differs slightly between the two apps, so navigate by the product heading, which
  // both render. Selecting by role and accessible name rather than by CSS is what makes one spec work
  // against two independent implementations.
  const name = (await productLinks.first().textContent())?.trim() ?? '';
  await productLinks.first().click().catch(async () => firstProduct.click());

  await expect(page.getByRole('button', { name: /add to basket|adding/i })).toBeVisible();

  await chooseAnAvailableSize(page);
  await page.getByRole('button', { name: 'Add to basket' }).click();

  // Scoped by its text. The product page now has TWO live regions - this confirmation and the
  // per-variant stock line - and both are legitimately role="status", so the spec has to say which.
  await expect(
    page.getByRole('status').filter({ hasText: 'Added to your basket' }),
  ).toBeVisible();

  return name;
}

/**
 * Places an order and returns its reference.
 *
 * Every test that needs an order calls this rather than relying on an earlier test having placed one.
 * Playwright runs specs in PARALLEL, so "the test above already did it" is not merely fragile - it is
 * a race, and the first version of this file failed exactly that way.
 */
async function placeAnOrder(page: Page, recipient: string): Promise<string> {
  await emptyBasket(page);
  await addFirstProduct(page);

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

test.describe('basket', () => {
  test('is not reachable anonymously and prompts to sign in', async ({ page }) => {
    await page.goto('/basket');

    await expect(page.getByRole('heading', { name: 'Your basket', level: 1 })).toBeVisible();
    await expect(page.getByText('Sign in to see the items in your basket.')).toBeVisible();
  });

  test('the basket and orders links only appear once signed in', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('banner').getByRole('link', { name: /^Basket/ })).toBeHidden();

    await signIn(page, 'customer');

    await expect(page.getByRole('banner').getByRole('link', { name: /^Basket/ })).toBeVisible();
    await expect(page.getByRole('banner').getByRole('link', { name: 'Orders' })).toBeVisible();
  });

  test('an empty basket offers a way out rather than a dead end', async ({ page }) => {
    await signIn(page, 'customer');
    await emptyBasket(page);

    await expect(page.getByText('Your basket is empty.')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Browse products' })).toBeVisible();
  });

  test('a product can be added and appears in the basket', async ({ page }) => {
    await signIn(page, 'customer');
    await emptyBasket(page);

    const name = await addFirstProduct(page);

    await page.goto('/basket');

    // The line links back to the product it came from. Asserted through the list's accessible name
    // rather than a table cell: the basket is a list of products with pictures, not a grid, and a
    // spec that asserts the LAYOUT rather than the content breaks every time the design changes
    // while telling you nothing about whether the basket works.
    await expect(
      page.getByRole('list', { name: 'Items in your basket' }).getByRole('link', { name }),
    ).toBeVisible();

    await expect(page.getByRole('status')).toContainText('1 item');
  });

  test('changing the quantity updates the total', async ({ page }) => {
    await signIn(page, 'catalogmgr');
    await emptyBasket(page);
    await addFirstProduct(page);

    await page.goto('/basket');

    const quantity = page.getByRole('spinbutton', { name: /quantity for/i }).first();
    await quantity.fill('3');

    await expect(page.getByRole('status')).toContainText('3 items');
  });

  test('setting a quantity of zero removes the line', async ({ page }) => {
    await signIn(page, 'support');
    await emptyBasket(page);
    await addFirstProduct(page);

    await page.goto('/basket');

    const quantity = page.getByRole('spinbutton', { name: /quantity for/i }).first();
    await quantity.fill('0');

    // The server treats 0 as "remove", so the client does not have to special-case it.
    await expect(page.getByText('Your basket is empty.')).toBeVisible({ timeout: 15_000 });
  });

  test('the basket warns that prices are confirmed at checkout', async ({ page }) => {
    await signIn(page, 'customer');
    await emptyBasket(page);
    await addFirstProduct(page);

    await page.goto('/basket');

    // Said plainly, not buried. Every line is re-priced from the catalogue when the order is placed,
    // and a customer who then sees a different total deserves to have been warned.
    await expect(
      page.getByText('Prices are confirmed when you place your order'),
    ).toBeVisible();
  });
});

/**
 * Serial, deliberately.
 *
 * Only three seed users hold `order:write` — customer, ordermgr and administrator — because support
 * and catalog-manager are not meant to buy things on the shop's behalf. That is correct least
 * privilege, and it leaves fewer users than there are tests here, so two specs would otherwise share a
 * basket and race each other while emptying it.
 *
 * Running them one at a time is the honest fix. The alternative — a seed user per test — would make the
 * realm export a function of the test suite, which is the tail wagging the dog.
 */
test.describe.configure({ mode: 'serial' });

test.describe('checkout and orders', () => {
  test('checkout refuses an empty basket rather than creating an empty order', async ({ page }) => {
    await signIn(page, 'customer');
    await emptyBasket(page);

    await page.goto('/checkout');

    await expect(page.getByText('Your basket is empty.')).toBeVisible();
  });

  test('an order can be placed and shows a confirmation', async ({ page }) => {
    await signIn(page, 'ordermgr');
    await emptyBasket(page);
    await addFirstProduct(page);

    await page.goto('/checkout');

    await page.getByLabel('Recipient').fill('Casey Customer');
    await page.getByLabel('Address line 1').fill('12 Rosewood Avenue');
    await page.getByLabel('City').fill('Bristol');
    await page.getByLabel('Postcode').fill('BS1 4TP');

    await page.getByRole('button', { name: 'Place order' }).click();

    // Redirected to the order, with a confirmation. Arriving at a bare detail page would give no sign
    // anything had succeeded.
    await expect(page.getByRole('status').first()).toContainText('your order is confirmed', {
      timeout: 20_000,
    });
    await expect(page.getByRole('heading', { level: 1 })).toContainText('Order ORD-');
  });

  test('placing an order empties the basket', async ({ page }) => {
    await signIn(page, 'customer');
    await placeAnOrder(page, 'Casey Customer');

    // Otherwise a customer who refreshes after checkout sees the items they have just bought still
    // sitting there, and buys them again.
    await page.goto('/basket');
    await expect(page.getByText('Your basket is empty.')).toBeVisible({ timeout: 15_000 });
  });

  test('a placed order appears in the order history', async ({ page }) => {
    await signIn(page, 'administrator');
    const reference = await placeAnOrder(page, 'Ada Admin');

    await page.goto('/orders');
    await expect(page.getByRole('link', { name: reference })).toBeVisible();

    await expect(page.getByRole('heading', { name: 'Your orders', level: 1 })).toBeVisible();
    await expect(page.getByRole('link', { name: /^ORD-/ }).first()).toBeVisible();
  });

  test('an order shows its status timeline as an ordered list', async ({ page }) => {
    await signIn(page, 'ordermgr');
    await placeAnOrder(page, 'Olly Orders');

    // A <ol>, so a screen reader announces the sequence. A row of styled divs reads as unrelated
    // words and conveys no progression at all.
    await expect(page.getByRole('list').filter({ hasText: 'Order placed' }).first()).toBeVisible();
    await expect(page.getByText('Order placed', { exact: true }).first()).toBeVisible();
  });

  test('an order records the delivery address as it was at the time', async ({ page }) => {
    await signIn(page, 'customer');
    await placeAnOrder(page, 'Casey Customer');

    await expect(page.getByRole('heading', { name: 'Delivery address', level: 2 })).toBeVisible();
    await expect(
      page.getByText('Recorded as it was when you ordered'),
    ).toBeVisible();
  });

  test('a submitted order can be cancelled by the customer', async ({ page }) => {
    await signIn(page, 'customer');
    await placeAnOrder(page, 'Casey Customer');

    await page.getByRole('button', { name: 'Cancel order' }).click();

    await expect(page.getByText('You cancelled this order').first()).toBeVisible({
      timeout: 15_000,
    });

    // The aggregate decides this, not the client: once cancelled there is nothing left to cancel.
    await expect(page.getByRole('button', { name: 'Cancel order' })).toBeHidden();
  });

  test('an unknown order id shows not-found rather than an error', async ({ page }) => {
    await signIn(page, 'customer');

    // A valid GUID that is not anybody's order. The server returns 404 for another customer's order
    // too - distinguishing the two would confirm to an attacker that the id is real.
    await page.goto('/orders/00000000-0000-0000-0000-000000000001');

    await expect(page.getByRole('heading', { name: 'Order not found', level: 1 })).toBeVisible({
      timeout: 15_000,
    });
  });
});
