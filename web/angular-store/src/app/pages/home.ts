import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { Auth } from '../auth/auth';
import {
  CatalogService,
  groupIntoDepartments,
  stockLevel,
  type Category,
  type ProductSummary,
} from '../core/catalog';
import { formatMoney } from '../core/formatting';
import { Icon, type IconName } from '../icon';

/**
 * The shopfront.
 *
 * Content is word-for-word identical to the React `HomePage`: the shared Playwright specs assert on
 * visible text, so any difference fails the parity run.
 *
 * ---
 * **Everything here is a real query.** The hero images, the category counts and the featured row all
 * come from the Catalog service — nothing is hard-coded to make the page look full. A landing page
 * built from fixtures is the one page that never catches a broken API.
 *
 * ---
 * **React/Angular divergence** (docs/react-vs-angular.md).
 *
 * React declares two `useQuery` calls and gets caching, deduping and loading state from TanStack Query.
 * Angular loads both in the constructor into signals and owns the states by hand — which is fine for a
 * page with two requests and no refetching, and is the same trade recorded since Phase 4.
 */
const REASSURANCE: readonly { icon: IconName; title: string; detail: string }[] = [
  { icon: 'truck', title: 'Free delivery over £50', detail: 'Dispatched within one working day.' },
  { icon: 'shield', title: '30-day returns', detail: 'Unused and in its original packaging.' },
  {
    icon: 'tag',
    title: 'Prices confirmed at checkout',
    detail: 'Always the current price, never a stale one.',
  },
];

@Component({
  selector: 'app-home-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Icon],
  template: `
    @if (auth.isLoading()) {
      <div class="centred" aria-busy="true" aria-live="polite">
        <p class="lede">Signing you in…</p>
      </div>
    } @else {
      <div class="stack">
        @if (auth.error(); as message) {
          <!-- role="alert" is announced immediately, which is what an authentication failure
               warrants. -->
          <div class="card" role="alert">
            <h2 style="margin-top: 0">Sign-in failed</h2>
            <p class="muted">{{ message }}</p>
          </div>
        }

        <section class="hero">
          <div>
            <h1 class="hero__title">Everyday things, properly made</h1>
            <p class="hero__lede">
              Clothing, drinkware and stationery from three independent brands.
            </p>

            <div class="row">
              <a class="btn btn--primary" routerLink="/products">Shop all products</a>

              @if (!auth.isAuthenticated()) {
                <button type="button" class="btn btn--secondary" (click)="auth.signIn()">
                  Sign in
                </button>
              }
            </div>
          </div>

          <!-- Four real products, so the hero is never a picture of a shop that does not exist.
               aria-hidden because the featured section below says the same thing in text. -->
          <div class="hero__art" aria-hidden="true">
            @for (product of featured().slice(0, 4); track product.id) {
              <img [src]="product.imageUrl ?? '/img/placeholder.svg'" alt="" />
            }
          </div>
        </section>

        <!-- Three things a shopper looks for before browsing. The icons are decorative; the text
             carries the message. -->
        <ul class="grid grid--3 plain-list">
          @for (item of reassurance; track item.title) {
            <li class="card row">
              <app-icon [name]="item.icon" />
              <span class="stack--tight">
                <strong>{{ item.title }}</strong>
                <span class="muted small">{{ item.detail }}</span>
              </span>
            </li>
          }
        </ul>

        @if (departments().length > 0) {
          <section aria-labelledby="categories-heading">
            <div class="section-head">
              <h2 id="categories-heading">Shop by department</h2>
              <a class="nav-link" routerLink="/products">
                All products <app-icon name="chevronRight" variant="icon--sm" />
              </a>
            </div>

            <ul class="grid grid--2 plain-list">
              @for (department of departments(); track department.id) {
                <li class="card department">
                  <h3 class="department__head">
                    <a routerLink="/products" [queryParams]="{ category: department.slug }">{{
                      department.name
                    }}</a>
                    <span class="muted small">{{ department.productCount }} products</span>
                  </h3>

                  <!-- The children, so the way in is one click rather than a category page and then
                       a filter. This is the whole reason a shop shows you its departments at all. -->
                  <div class="department__links">
                    @for (child of department.children; track child.id) {
                      <a
                        class="department__chip"
                        routerLink="/products"
                        [queryParams]="{ category: child.slug }"
                      >
                        {{ child.name }}
                        <span class="category-rail__count">{{ child.productCount }}</span>
                      </a>
                    }
                  </div>
                </li>
              }
            </ul>
          </section>
        }

        @if (featured().length > 0) {
          <section aria-labelledby="featured-heading">
            <div class="section-head">
              <h2 id="featured-heading">In stock now</h2>
            </div>

            <ul class="grid grid--4 plain-list">
              @for (product of featured(); track product.id) {
                <li class="card product-card">
                  <div class="product-media">
                    <img
                      class="product-media__img"
                      [src]="product.imageUrl ?? '/img/placeholder.svg'"
                      alt=""
                      aria-hidden="true"
                      loading="lazy"
                      width="400"
                      height="300"
                    />
                  </div>

                  <div class="product-card__body">
                    <p class="product-card__brand">{{ product.brandName }}</p>
                    <h3 class="product-card__name">
                      <a [routerLink]="['/products', product.id]" class="product-card__link">{{
                        product.name
                      }}</a>
                    </h3>
                  </div>

                  <div class="product-card__footer">
                    <span class="price">{{ money(product) }}</span>
                    <span class="badge badge--{{ stockOf(product).level }}">{{
                      stockOf(product).label
                    }}</span>
                  </div>
                </li>
              }
            </ul>
          </section>
        }

        <!-- Kept because it is the point of this repository; moved below the shopping because a
             customer did not come here for it. -->
        @if (auth.isAuthenticated()) {
          <section class="card stack" aria-labelledby="permissions-heading">
            <h2 id="permissions-heading" style="margin-top: 0">
              Signed in as {{ auth.displayName() }}
              <span class="badge badge--info">{{ permissions().length }} permissions</span>
            </h2>

            <p class="muted">
              Granted by composite roles in Keycloak, not assigned to this account directly. They decide
              what this page shows — the server enforces the same rules independently.
            </p>

            <div class="chips">
              @for (permission of permissions(); track permission) {
                <span class="chip">{{ permission }}</span>
              }
            </div>
          </section>
        } @else {
          <section class="card stack" aria-labelledby="try-heading">
            <h2 id="try-heading" style="margin-top: 0">Try it</h2>
            <p class="muted">
              Every account uses the password <code>Passw0rd!</code>. Sign in as different users and
              watch what the shop lets you do change.
            </p>
            <ul class="muted" style="margin: 0; padding-inline-start: 1.25rem">
              <li><code>customer</code> — browse, buy, track orders</li>
              <li><code>support</code> — read-only, and deliberately cannot check out</li>
              <li><code>administrator</code> — everything, including the back office</li>
            </ul>
          </section>
        }
      </div>
    }
  `,
})
export class HomePage {
  protected readonly auth = inject(Auth);
  private readonly catalog = inject(CatalogService);

  protected readonly reassurance = REASSURANCE;
  protected readonly featured = signal<readonly ProductSummary[]>([]);
  protected readonly categories = signal<readonly Category[]>([]);

  protected readonly money = (product: ProductSummary) =>
    formatMoney({ amount: product.price, currency: product.currency });

  protected readonly stockOf = (product: ProductSummary) => stockLevel(product.stockOnHand);

  protected readonly permissions = computed(() =>
    [...(this.auth.user()?.permissions ?? [])].sort(),
  );

  /**
   * The shop, by department.
   *
   * Departments only, each showing what is inside it - the way a shop is actually organised, and the
   * thing a flat list of six tiles could not express. A customer thinking "I want a hoodie" and one
   * thinking "show me the clothing" both find their way in one look.
   *
   * Empty departments are dropped rather than advertised. The counts include children now
   * (GetCategoriesAsync rolls them up), so a department reading zero genuinely has nothing in it.
   */
  protected readonly departments = computed(() =>
    groupIntoDepartments(this.categories()).filter((department) => department.productCount > 0),
  );

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      // In stock only, because a shopfront leading with sold-out items wastes a click. Sorted by
      // name so the row is stable between visits rather than reshuffling.
      const [products] = await Promise.all([
        this.catalog.searchProducts({ inStockOnly: true, pageSize: 4, sortBy: 'name' }),
        this.catalog.loadCategories(),
      ]);

      this.featured.set(products.items);
      this.categories.set(this.catalog.categories());
    } catch {
      // A shopfront that cannot reach the catalogue still shows its hero, its reassurance strip and
      // its sign-in prompt. Failing the whole page over a missing product row would be worse than
      // showing slightly less of it.
    }
  }
}
