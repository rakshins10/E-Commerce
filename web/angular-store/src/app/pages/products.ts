import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';

import {
  CatalogService,
  groupIntoDepartments,
  stockLevel,
  type PagedResult,
  type ProductFilters,
  type ProductSummary,
} from '../core/catalog';
import { formatMoney } from '../core/formatting';

/**
 * Product browsing: search, filter, sort, page.
 *
 * Behaviourally identical to the React `ProductsPage` — same URL parameters,
 * same labels, same states — so the shared Playwright specs pass against both.
 *
 * ---
 * **React/Angular divergence** (docs/react-vs-angular.md).
 *
 * Filters live in the URL in both apps, but the mechanics differ. React reads
 * `useSearchParams` and derives with `useMemo`; Angular converts the route's
 * `queryParamMap` observable into a signal with `toSignal`, and everything
 * downstream is `computed`. Angular's version needs no dependency array, which
 * removes a class of stale-closure bug — but it does need the RxJS-to-signal
 * bridge, because the router is still Observable-based.
 *
 * On data fetching React is ahead: TanStack Query gives caching and
 * keep-previous-data for free, where this component wires an `effect` by hand.
 */
@Component({
  selector: 'app-products-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <div class="stack">
      <h1 class="page-title">Products</h1>

      <!-- Top-level, above the filter bar, because that is how a clothing shop is organised - a
           shopper decides "menswear" before they decide "hoodies". It is an attribute rather than a
           branch of the taxonomy (ADR-0020), but presenting it as a category-level choice is what
           makes it findable. -->
      @if (facets(); as f) {
        @if (f.audiences.length > 1) {
          <nav class="audience-tabs" aria-label="Shop for">
            <a
              class="audience-tab"
              routerLink="/products"
              [queryParams]="audienceParams('')"
              queryParamsHandling="merge"
              [attr.aria-current]="filters().audience === '' ? 'true' : null"
              >Everyone</a
            >

            @for (option of f.audiences; track option.value) {
              <a
                class="audience-tab"
                routerLink="/products"
                [queryParams]="audienceParams(option.value)"
                queryParamsHandling="merge"
                [attr.aria-current]="filters().audience === option.value ? 'true' : null"
                >{{ option.value }}</a
              >
            }
          </nav>
        }
      }

      <section class="card" aria-labelledby="filters-heading">
        <h2 id="filters-heading" class="visually-hidden">Filter products</h2>

        <div class="filters">
          <div class="field">
            <label for="search">Search</label>
            <input
              id="search"
              type="search"
              class="input"
              placeholder="Name, SKU or description"
              [value]="searchInput()"
              (input)="onSearchInput($event)"
            />
          </div>

          <!-- An optgroup, not a hand-drawn indent.

               (No backticks in this comment, or anywhere else in this template: an Angular inline
               template IS a TypeScript template literal, so one backtick ends the string and the
               compiler reports NG1010 "template must be a string" pointing at the decorator rather
               than at the character.)


               This used to prefix every child with an em dash - "- T-shirts (3)" - which is what you
               reach for when you want a tree in a control that does not have one. It renders as a
               stray character with no meaning, a screen reader announces it, and it still does not
               say which parent the child belongs to.

               optgroup is the real thing: the browser indents it, assistive technology announces
               the group name alongside the option, and the department stops being a selectable row
               that looked like an option but read like a heading. -->
          <div class="field">
            <label for="category">Category</label>
            <select id="category" class="input" [value]="filters().category ?? ''" (change)="onFilter('category', $event)">
              <option value="">All categories</option>

              @for (department of departments(); track department.id) {
                <optgroup [label]="department.name">
                  <!-- The department itself stays selectable — the server rolls its children up, so
                       "everything in Clothing" is a real and useful query. -->
                  <option [value]="department.slug">
                    All {{ department.name.toLowerCase() }} ({{ department.productCount }})
                  </option>

                  @for (child of department.children; track child.id) {
                    <option [value]="child.slug">{{ child.name }} ({{ child.productCount }})</option>
                  }
                </optgroup>
              }
            </select>
          </div>

          <div class="field">
            <label for="brand">Brand</label>
            <select id="brand" class="input" [value]="filters().brand ?? ''" (change)="onFilter('brand', $event)">
              <option value="">All brands</option>
              @for (brand of catalog.brands(); track brand.id) {
                <option [value]="brand.slug">{{ brand.name }} ({{ brand.productCount }})</option>
              }
            </select>
          </div>

          <!-- Size and colour filter across the WHOLE catalogue, so "show me everything in Large"
               is one click. The counts are of products, not variants - "Navy (2)" has to mean two
               things you can click through to. -->
          @if (facets(); as f) {
            @if (f.sizes.length > 0) {
              <div class="field">
                <label for="size">Size</label>
                <select id="size" class="input" [value]="filters().size ?? ''" (change)="onFilter('size', $event)">
                  <option value="">All sizes</option>
                  @for (option of f.sizes; track option.value) {
                    <option [value]="option.value">{{ option.value }} ({{ option.productCount }})</option>
                  }
                </select>
              </div>
            }

            @if (f.colours.length > 0) {
              <div class="field">
                <label for="colour">Colour</label>
                <select id="colour" class="input" [value]="filters().colour ?? ''" (change)="onFilter('colour', $event)">
                  <option value="">All colours</option>
                  @for (option of f.colours; track option.value) {
                    <option [value]="option.value">{{ option.value }} ({{ option.productCount }})</option>
                  }
                </select>
              </div>
            }
          }

          <div class="field">
            <label for="sort">Sort by</label>
            <select id="sort" class="input" [value]="sortValue()" (change)="onSort($event)">
              <option value="name:asc">Name (A–Z)</option>
              <option value="name:desc">Name (Z–A)</option>
              <option value="price:asc">Price (low to high)</option>
              <option value="price:desc">Price (high to low)</option>
              <option value="brand:asc">Brand</option>
            </select>
          </div>

          <div class="field field--checkbox">
            <input
              id="inStockOnly"
              type="checkbox"
              [checked]="filters().inStockOnly"
              (change)="onInStockOnly($event)"
            />
            <label for="inStockOnly">In stock only</label>
          </div>

          @if (hasFilters()) {
            <button type="button" class="btn btn--secondary" (click)="clearFilters()">Clear filters</button>
          }
        </div>
      </section>

      <div class="browse-layout">
        <!-- The taxonomy, laid out rather than folded into a dropdown.

             A shopper who has not decided yet cannot browse a dropdown — it has to be opened, read
             and closed again to see anything, and it shows one department at a time. This shows the
             whole shop at once: every department, what is inside it, and how many products each
             holds.

             They are real links, not click handlers. Middle-click opens a category in a new tab, the
             status bar shows where each one goes, and every one is an address that can be sent to
             somebody else — none of which a button gives you. -->
        <nav class="category-rail" aria-label="Categories">
          <h2 class="category-rail__title">Categories</h2>

          <ul class="plain-list">
            <li>
              <a
                class="category-rail__link"
                routerLink="/products"
                [queryParams]="categoryParams('')"
                queryParamsHandling="merge"
                [attr.aria-current]="filters().category === '' ? 'true' : null"
                >All products</a
              >
            </li>
          </ul>

          @for (department of departments(); track department.id) {
            <div>
              <h3 class="category-rail__heading">
                <a
                  class="category-rail__link"
                  routerLink="/products"
                  [queryParams]="categoryParams(department.slug)"
                  queryParamsHandling="merge"
                  [attr.aria-current]="filters().category === department.slug ? 'true' : null"
                >
                  {{ department.name }}
                  <span class="category-rail__count">{{ department.productCount }}</span>
                </a>
              </h3>

              @if (department.children.length > 0) {
                <ul class="plain-list">
                  @for (child of department.children; track child.id) {
                    <li>
                      <a
                        class="category-rail__link"
                        routerLink="/products"
                        [queryParams]="categoryParams(child.slug)"
                        queryParamsHandling="merge"
                        [attr.aria-current]="filters().category === child.slug ? 'true' : null"
                      >
                        {{ child.name }}
                        <span class="category-rail__count">{{ child.productCount }}</span>
                      </a>
                    </li>
                  }
                </ul>
              }
            </div>
          }
        </nav>

        <div class="stack">
      <!-- aria-live so a screen-reader user hears the count change after
           filtering; without it, filtering is silent and appears to do nothing. -->
      <p class="muted" aria-live="polite" role="status">
        @if (isLoading()) {
          Loading products…
        } @else if (result(); as r) {
          {{ r.totalCount }} product{{ r.totalCount === 1 ? '' : 's' }}
        }
      </p>

      @if (error(); as message) {
        <div class="card" role="alert">
          <h2 style="margin-top: 0">Could not load products</h2>
          <p class="muted">{{ message }}</p>
          <button type="button" class="btn btn--primary" (click)="reload()">Try again</button>
        </div>
      }

      @if (isLoading() && !result()) {
        <div class="grid grid--3" aria-hidden="true">
          @for (skeleton of skeletons; track skeleton) {
            <div class="card product-card product-card--skeleton"></div>
          }
        </div>
      }

      @if (result(); as r) {
        @if (r.items.length === 0) {
          <div class="card centred-block">
            <h2 style="margin-top: 0">No products match</h2>
            <p class="muted">Try a different search or clear the filters.</p>
          </div>
        } @else {
          <!-- Named, so "the products" is a thing that can be pointed at. The page now has product
               headings AND department headings in the rail, and without a name on this list the only
               way to say "a product" is by position in the document - which is exactly how a spec
               ends up clicking a category. -->
          <ul class="grid grid--3 product-grid" aria-label="Products">
            @for (product of r.items; track product.id) {
              <li class="card product-card">
                <div class="product-media">
                  <!--
                    alt="" and aria-hidden: the illustration carries no information the product name
                    does not already give, and a screen reader announcing "Ceramic Mug illustration"
                    directly above the text "Ceramic Mug" is repetition, not description.
                  -->
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
                    <!--
                      Only the NAME is the link, so the accessible name is the product and nothing
                      else. The ::after in the stylesheet stretches the hit area to the whole card.
                    -->
                    <a [routerLink]="['/products', product.id]" class="product-card__link">{{
                      product.name
                    }}</a>
                  </h3>
                </div>

                <div class="product-card__footer">
                  <span class="price">{{ money(product) }}</span>
                  <span class="badge badge--{{ stock(product).level }}">{{ stock(product).label }}</span>
                </div>
              </li>
            }
          </ul>
        }

        @if (r.totalPages > 1) {
          <nav class="pager" aria-label="Pagination">
            <button type="button" class="btn btn--secondary" [disabled]="!r.hasPrevious" (click)="goToPage(r.page - 1)">
              Previous
            </button>
            <span class="muted">Page {{ r.page }} of {{ r.totalPages }}</span>
            <button type="button" class="btn btn--secondary" [disabled]="!r.hasNext" (click)="goToPage(r.page + 1)">
              Next
            </button>
          </nav>
        }
      }
        </div>
      </div>
    </div>
  `,
})
export class ProductsPage {
  protected readonly catalog = inject(CatalogService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly skeletons = Array.from({ length: 6 }, (_, i) => i);

  // The router is Observable-based, so toSignal bridges it. Everything
  // downstream is then plain signal arithmetic.
  private readonly queryParams = toSignal(this.route.queryParamMap, { requireSync: true });

  protected readonly filters = computed<ProductFilters>(() => {
    const params = this.queryParams();
    return {
      search: params.get('search') ?? '',
      category: params.get('category') ?? '',
      brand: params.get('brand') ?? '',
      audience: params.get('audience') ?? '',
      size: params.get('size') ?? '',
      colour: params.get('colour') ?? '',
      inStockOnly: params.get('inStockOnly') === 'true',
      sortBy: (params.get('sortBy') as ProductFilters['sortBy']) ?? 'name',
      sortDescending: params.get('sortDescending') === 'true',
      page: Number(params.get('page') ?? '1'),
      pageSize: 12,
    };
  });

  /** Sizes, colours and audiences, from the service's cache. */
  protected readonly facets = this.catalog.facets;

  /** The query parameters for an audience, keeping every other filter. Same rule as categoryParams. */
  protected audienceParams(value: string): Record<string, string | null> {
    return { audience: value || null, page: null };
  }

  /** The taxonomy, grouped once here rather than unpicked in the template. */
  protected readonly departments = computed(() => groupIntoDepartments(this.catalog.categories()));

  /**
   * The query parameters for a category, keeping every other filter.
   *
   * Paired with queryParamsHandling="merge", so the rail writes to the same URL the select does -
   * a link is not a second code path, it sets the same ?category=. null REMOVES a parameter
   * under merge, which is how "All products" clears the filter and how every link drops the page
   * number: page 3 of Clothing is not page 3 of Hoodies.
   */
  protected categoryParams(slug: string): Record<string, string | null> {
    return { category: slug || null, page: null };
  }

  protected readonly sortValue = computed(
    () => `${this.filters().sortBy}:${this.filters().sortDescending ? 'desc' : 'asc'}`,
  );

  protected readonly hasFilters = computed(() => {
    const f = this.filters();
    return Boolean(f.search || f.category || f.brand || f.inStockOnly || f.audience || f.size || f.colour);
  });

  // The search box must feel instant, so it is local state debounced into the
  // URL. Writing every keystroke to the URL would spam history and fire a
  // request per character.
  protected readonly searchInput = signal('');
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  // `previousResult` is kept while the next page loads, so the grid does not
  // flash empty - hand-rolling what TanStack Query's keepPreviousData gives
  // React for free.
  protected readonly result = signal<PagedResult<ProductSummary> | null>(null);
  protected readonly isLoading = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.searchInput.set(this.filters().search ?? '');

    void this.catalog.loadCategories();
    void this.catalog.loadBrands();
    void this.catalog.loadFacets();

    // Refetch whenever the URL changes. The effect tracks `filters()`
    // automatically - no dependency array to keep in step.
    effect(() => {
      const current = this.filters();
      void this.load(current);
    });
  }

  private async load(filters: ProductFilters): Promise<void> {
    this.isLoading.set(true);
    this.error.set(null);

    try {
      this.result.set(await this.catalog.searchProducts(filters));
    } catch (cause) {
      this.error.set(cause instanceof Error ? cause.message : 'Unexpected error');
    } finally {
      this.isLoading.set(false);
    }
  }

  protected reload(): void {
    void this.load(this.filters());
  }

  protected money(product: ProductSummary): string {
    return formatMoney({ amount: product.price, currency: product.currency });
  }

  protected stock(product: ProductSummary) {
    return stockLevel(product.stockOnHand);
  }

  protected onSearchInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.searchInput.set(value);

    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.updateParams({ search: value, page: '1' }), 300);
  }

  protected onFilter(key: 'category' | 'brand' | 'size' | 'colour', event: Event): void {
    this.updateParams({ [key]: (event.target as HTMLSelectElement).value, page: '1' });
  }

  protected onSort(event: Event): void {
    const [sortBy, direction] = (event.target as HTMLSelectElement).value.split(':');
    this.updateParams({
      sortBy: sortBy!,
      sortDescending: direction === 'desc' ? 'true' : '',
      page: '1',
    });
  }

  protected onInStockOnly(event: Event): void {
    this.updateParams({
      inStockOnly: (event.target as HTMLInputElement).checked ? 'true' : '',
      page: '1',
    });
  }

  protected goToPage(page: number): void {
    this.updateParams({ page: String(page) });
  }

  protected clearFilters(): void {
    this.searchInput.set('');
    void this.router.navigate([], { queryParams: {}, replaceUrl: true });
  }

  private updateParams(changes: Record<string, string>): void {
    const queryParams: Record<string, string | null> = {};
    for (const [key, value] of Object.entries(changes)) {
      queryParams[key] = value || null; // null removes the parameter
    }

    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }
}
