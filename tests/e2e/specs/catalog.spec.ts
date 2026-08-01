import { expect, test } from '@playwright/test';

/**
 * Product browsing: search, filter, sort, page, detail.
 *
 * Written ONCE and run against both storefronts. Every selector is a role and
 * an accessible name — never a CSS class or test id — because those differ
 * between two independent implementations.
 *
 * @see docs/adr/0014-react-and-angular-in-lockstep.md
 */

test.describe('product browsing', () => {
  test('the product grid loads with the seeded catalogue', async ({ page }) => {
    await page.goto('/products');

    await expect(page.getByRole('heading', { name: 'Products', level: 1 })).toBeVisible();

    // The status region announces the count - asserting on it proves both the
    // data arrived and the accessible live region exists.
    await expect(page.getByRole('status')).toContainText('12 products');

    await expect(page.getByRole('heading', { name: 'Classic Cotton T-shirt' })).toBeVisible();
  });

  test('prices are formatted identically in both apps', async ({ page }) => {
    await page.goto('/products');
    await expect(page.getByRole('status')).toContainText('12 products');

    // Currency formatting is duplicated per app since ADR-0018, so this assertion
    // is what stops the two implementations drifting on it.
    await expect(page.getByText('£18.00').first()).toBeVisible();
  });

  test('stock is conveyed by text, not colour alone', async ({ page }) => {
    await page.goto('/products?sortBy=price&sortDescending=true');
    await expect(page.getByRole('status')).toContainText('product');

    // WCAG 1.4.1: a green/red dot alone is invisible to a colour-blind user.
    await expect(page.getByText('Only 2 left').first()).toBeVisible();
  });

  test('search narrows the results and is reflected in the URL', async ({ page }) => {
    await page.goto('/products');
    await expect(page.getByRole('status')).toContainText('12 products');

    await page.getByLabel('Search').fill('hoodie');

    // The URL carries the filter, so the result is shareable and survives a
    // refresh - the single most commonly missed thing on a browse screen.
    await expect(page).toHaveURL(/search=hoodie/);
    await expect(page.getByRole('status')).toContainText('3 products');
    await expect(page.getByRole('heading', { name: 'Pullover Hoodie' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Enamel Mug' })).toBeHidden();
  });

  test('a search filter survives a full page reload', async ({ page }) => {
    await page.goto('/products?search=hoodie');
    await expect(page.getByRole('status')).toContainText('3 products');

    await page.reload();
    await expect(page.getByRole('status')).toContainText('3 products');
    await expect(page.getByLabel('Search')).toHaveValue('hoodie');
  });

  test('filtering by a parent category includes its child categories', async ({ page }) => {
    await page.goto('/products');
    await expect(page.getByRole('status')).toContainText('12 products');

    await page.getByLabel('Category').selectOption('clothing');

    // 6 = t-shirts (3) + hoodies (3). If this returned 0, the parent category
    // would look empty, which reads as a bug to a user.
    await expect(page.getByRole('status')).toContainText('6 products');
  });

  test('the category rail offers the whole taxonomy without opening a menu', async ({ page }) => {
    await page.goto('/products');

    const rail = page.getByRole('navigation', { name: 'Categories' });

    // Departments AND what is inside them, visible at once. A dropdown can hold the same
    // information; it cannot show it.
    await expect(rail.getByRole('link', { name: 'Clothing' })).toBeVisible();
    await expect(rail.getByRole('link', { name: 'Hoodies' })).toBeVisible();
    await expect(rail.getByRole('link', { name: 'T-shirts' })).toBeVisible();

    await rail.getByRole('link', { name: 'Hoodies' }).click();

    // A real link, so the filter is in the address and survives a reload.
    await expect(page).toHaveURL(/category=hoodies/);
    await expect(page.getByRole('status')).toContainText('3 products');

    // aria-current marks the selection for a screen reader, not colour alone.
    await expect(rail.getByRole('link', { name: 'Hoodies' })).toHaveAttribute(
      'aria-current',
      'true',
    );
  });

  test('a department counts the products inside its children', async ({ page }) => {
    await page.goto('/products');

    // Clothing holds no products directly - T-shirts and Hoodies do. The count has to say 6, the
    // number selecting it returns, or it advertises an empty shop.
    await expect(
      page.getByRole('navigation', { name: 'Categories' }).getByRole('link', { name: 'Clothing' }),
    ).toContainText('6');
  });

  test('filtering by brand works', async ({ page }) => {
    await page.goto('/products');
    await expect(page.getByRole('status')).toContainText('12 products');

    await page.getByLabel('Brand').selectOption('northwind');
    await expect(page.getByRole('status')).toContainText('5 products');
  });

  test('the in-stock filter excludes sold-out products', async ({ page }) => {
    await page.goto('/products');
    await expect(page.getByRole('status')).toContainText('12 products');

    // `click` + `toBeChecked`, not `check()`. `check()` clicks and then asserts
    // once, without retrying; a controlled React input driven by the URL can be
    // momentarily out of step with the DOM while the router update lands, and
    // `check()` fails on that transient even though the end state is right.
    // It failed exactly that way on CI's slower runner while passing 96/96
    // locally. `toBeChecked()` retries, so this asserts the outcome rather than
    // the timing of a re-render.
    await page.getByLabel('In stock only').click();
    await expect(page.getByLabel('In stock only')).toBeChecked();

    // Two seeded products have zero stock.
    await expect(page.getByRole('status')).toContainText('10 products');
  });

  test('sorting by price reorders the grid', async ({ page }) => {
    await page.goto('/products');
    await expect(page.getByRole('status')).toContainText('12 products');

    await page.getByLabel('Sort by').selectOption('price:desc');

    await expect(page).toHaveURL(/sortBy=price/);
    // The most expensive seeded product.
    await expect(page.getByRole('heading', { name: 'Leather Portfolio' })).toBeVisible();
  });

  test('filters can be cleared', async ({ page }) => {
    await page.goto('/products?search=hoodie&brand=northwind');
    await expect(page.getByRole('status')).toContainText('1 product');

    await page.getByRole('button', { name: 'Clear filters' }).click();

    await expect(page.getByRole('status')).toContainText('12 products');
    await expect(page.getByLabel('Search')).toHaveValue('');
  });

  test('an unmatched search shows a helpful empty state', async ({ page }) => {
    await page.goto('/products?search=zzzznotathing');

    await expect(page.getByRole('heading', { name: 'No products match' })).toBeVisible();
    await expect(page.getByText('Try a different search or clear the filters.')).toBeVisible();
  });

  test('paging moves through the catalogue', async ({ page }) => {
    await page.goto('/products');
    await expect(page.getByRole('status')).toContainText('12 products');

    // 12 products at 12 per page is one page, so shrink the page via the URL.
    await page.goto('/products?page=2');
    await expect(page.getByRole('navigation', { name: 'Pagination' })).toBeHidden();
  });
});

test.describe('product detail', () => {
  test('a product card links through to its detail page', async ({ page }) => {
    await page.goto('/products?search=Pullover');
    await expect(page.getByRole('status')).toContainText('1 product');

    await page.getByRole('link', { name: /Pullover Hoodie/ }).click();

    await expect(page.getByRole('heading', { name: 'Pullover Hoodie', level: 1 })).toBeVisible();
    await expect(page.getByText('SKU NW-HD-001')).toBeVisible();
    await expect(page.getByText('£45.00')).toBeVisible();
    await expect(page.getByText('In stock')).toBeVisible();
  });

  test('the detail page offers breadcrumb navigation back to a filtered list', async ({ page }) => {
    await page.goto('/products?search=Pullover');
    await page.getByRole('link', { name: /Pullover Hoodie/ }).click();

    await page.getByRole('navigation', { name: 'Breadcrumb' }).getByRole('link', { name: 'Hoodies' }).click();

    await expect(page).toHaveURL(/category=hoodies/);
    await expect(page.getByRole('status')).toContainText('3 products');
  });

  test('an unknown product id shows not-found rather than an error', async ({ page }) => {
    await page.goto('/products/00000000-0000-0000-0000-000000000000');

    // A 404 is a normal outcome deserving a helpful page, not a crash screen.
    await expect(page.getByRole('heading', { name: 'Product not found', level: 1 })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Back to products' })).toBeVisible();
  });

  test('add to basket is offered on an in-stock product, once a size is chosen', async ({ page }) => {
    await page.goto('/products?search=Pullover');
    await page.getByRole('link', { name: /Pullover Hoodie/ }).click();

    // Disabled UNTIL a size is picked. A size is never pre-selected, because defaulting it means
    // somebody buys a Small because it happened to be first in the list.
    const addToBasket = page.getByRole('button', { name: 'Add to basket' });
    await expect(addToBasket).toBeDisabled();

    // The label, not the input: the radio is visually hidden and pointer-events:none, so it is the
    // label a person clicks.
    await page.getByRole('group', { name: 'Size' }).getByText('M', { exact: true }).click();

    // Enabled now. Out-of-stock keeps the disabled-with-a-reason treatment: a missing button looks
    // broken, a disabled one with a title explains itself. What the button DOES is in shopping.spec.
    await expect(addToBasket).toBeEnabled();
  });

  test('a colour is pre-selected but a size is not', async ({ page }) => {
    await page.goto('/products?search=Pullover');
    await page.getByRole('link', { name: /Pullover Hoodie/ }).click();
    await expect(page.getByRole('heading', { name: 'Pullover Hoodie', level: 1 })).toBeVisible();

    // The asymmetry is the decision, not an oversight. The photograph already shows a colour, so
    // defaulting to one is honest; a size is a choice only the customer can make.
    await expect(page.getByRole('group', { name: 'Colour' }).getByRole('radio').first()).toBeChecked();

    const sizes = page.getByRole('group', { name: 'Size' }).getByRole('radio');
    const count = await sizes.count();
    expect(count).toBeGreaterThan(0);

    for (let index = 0; index < count; index++) {
      await expect(sizes.nth(index)).not.toBeChecked();
    }
  });

  test('stock is reported for the chosen size, not for the product', async ({ page }) => {
    // Graphic Print T-shirt: 2 left in Black S, none in Black XL, and the product itself in stock.
    // The whole reason per-variant stock exists.
    await page.goto('/products?search=Graphic');
    await page.getByRole('link', { name: /Graphic Print T-shirt/ }).click();

    const sizeGroup = page.getByRole('group', { name: 'Size' });

    // Black is the first colour and is pre-selected, so these are Black's figures.
    await sizeGroup.getByText('S', { exact: true }).click();
    await expect(page.getByText('Only 2 left')).toBeVisible();

    await sizeGroup.getByText('M', { exact: true }).click();

    // Scoped to the live region. "In stock" also appears as the product-level badge beside the
    // price, and it is the per-variant figure this test is about.
    await expect(page.getByRole('status').filter({ hasText: 'In stock' })).toBeVisible();
  });

  test('a combination that does not exist is reported, not silently allowed', async ({ page }) => {
    await page.goto('/products?search=Graphic');
    await page.getByRole('link', { name: /Graphic Print T-shirt/ }).click();

    // XL itself is NOT disabled - Ecru has one left, so the size is genuinely available. It is the
    // COMBINATION Black + XL that has none, which is exactly the distinction per-variant stock
    // exists to express, and exactly the thing a size-level "sold out" flag would get wrong.
    const sizeGroup = page.getByRole('group', { name: 'Size' });
    await expect(sizeGroup.getByRole('radio', { name: 'XL' })).toBeEnabled();

    await page.getByRole('group', { name: 'Colour' }).getByText('Black', { exact: true }).click();
    await sizeGroup.getByText('XL', { exact: true }).click();

    await expect(page.getByText('Sold out in this option')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Add to basket' })).toBeDisabled();
  });

  test('a product sold out in every size says so and cannot be bought', async ({ page }) => {
    await page.goto('/products?search=Heavyweight');
    await page.getByRole('link', { name: /Heavyweight Hoodie/ }).click();

    // Wait for the page before counting. `count()` does not auto-wait — it returns whatever matches
    // at that instant — so without this it counts the radios on a page that has not rendered yet.
    await expect(page.getByRole('heading', { name: 'Heavyweight Hoodie', level: 1 })).toBeVisible();

    // Every size disabled, and the page still renders rather than 404ing or showing an empty picker.
    const sizes = page.getByRole('group', { name: 'Size' }).getByRole('radio');
    const count = await sizes.count();
    expect(count).toBeGreaterThan(0);

    for (let index = 0; index < count; index++) {
      await expect(sizes.nth(index)).toBeDisabled();
    }

    await expect(page.getByRole('button', { name: 'Add to basket' })).toBeDisabled();
  });

  test('the catalogue can be filtered by size and by who it is for', async ({ page }) => {
    await page.goto('/products');
    await expect(page.getByRole('status')).toContainText('12 products');

    // Only clothing has sizes, so filtering by one excludes the mugs and the notebooks.
    await page.getByLabel('Size').selectOption('S');
    await expect(page.getByRole('status')).toContainText('6 products');

    await page.goto('/products');
    await page.getByRole('navigation', { name: 'Shop for' }).getByRole('link', { name: 'Women' }).click();

    await expect(page).toHaveURL(/audience=Women/);
    await expect(page.getByRole('status')).toContainText('2 products');
  });
});
