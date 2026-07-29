import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';

import {
  CatalogService,
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

          <div class="field">
            <label for="category">Category</label>
            <select id="category" class="input" [value]="filters().category ?? ''" (change)="onFilter('category', $event)">
              <option value="">All categories</option>
              @for (category of catalog.categories(); track category.id) {
                <option [value]="category.slug">
                  {{ category.parentSlug ? '— ' + category.name : category.name }} ({{ category.productCount }})
                </option>
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
          <ul class="grid grid--3 product-grid">
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
      inStockOnly: params.get('inStockOnly') === 'true',
      sortBy: (params.get('sortBy') as ProductFilters['sortBy']) ?? 'name',
      sortDescending: params.get('sortDescending') === 'true',
      page: Number(params.get('page') ?? '1'),
      pageSize: 12,
    };
  });

  protected readonly sortValue = computed(
    () => `${this.filters().sortBy}:${this.filters().sortDescending ? 'desc' : 'asc'}`,
  );

  protected readonly hasFilters = computed(() => {
    const f = this.filters();
    return Boolean(f.search || f.category || f.brand || f.inStockOnly);
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

  protected onFilter(key: 'category' | 'brand', event: Event): void {
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
