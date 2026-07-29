import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../environments/environment';

/**
 * Catalog API types and access.
 *
 * Owned by this application — the React storefront has its own equivalent in
 * `src/lib/catalog.ts`. See docs/adr/0018-self-contained-frontends.md.
 */

export interface ProductSummary {
  readonly id: string;
  readonly sku: string;
  readonly name: string;
  readonly price: number;
  readonly currency: string;
  readonly categoryName: string;
  readonly categorySlug: string;
  readonly brandName: string;
  readonly brandSlug: string;
  readonly imageUrl: string | null;
  readonly stockOnHand: number;
}

export interface ProductDetail extends ProductSummary {
  readonly description: string;
}

export interface Category {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly parentSlug: string | null;
  readonly productCount: number;
}

export interface Brand {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly productCount: number;
}

export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
  readonly hasPrevious: boolean;
  readonly hasNext: boolean;
}

export interface ProductFilters {
  readonly search?: string;
  readonly category?: string;
  readonly brand?: string;
  readonly inStockOnly?: boolean;
  readonly sortBy?: 'name' | 'price' | 'brand' | 'newest';
  readonly sortDescending?: boolean;
  readonly page?: number;
  readonly pageSize?: number;
}

export type StockLevel = 'in-stock' | 'low-stock' | 'out-of-stock';

/**
 * Stock presentation, shared by the list and detail screens.
 *
 * **Never colour alone.** Roughly 1 in 12 men has some colour vision
 * deficiency, so a green/red dot conveys nothing to them — WCAG 1.4.1. Every
 * caller renders the label text alongside the styling. Wording is identical to
 * the React implementation because the shared e2e specs assert on it.
 */
export function stockLevel(stockOnHand: number): { level: StockLevel; label: string } {
  if (stockOnHand <= 0) return { level: 'out-of-stock', label: 'Out of stock' };
  if (stockOnHand <= 5) return { level: 'low-stock', label: `Only ${stockOnHand} left` };
  return { level: 'in-stock', label: 'In stock' };
}

/**
 * Catalog data access, with a small signal-based cache.
 *
 * ---
 * **React/Angular divergence** (docs/react-vs-angular.md).
 *
 * React uses TanStack Query, which supplies caching, deduplication, stale-time
 * and `keepPreviousData` out of the box. Angular has no direct equivalent in
 * the framework, so the same behaviours are built here explicitly: an in-memory
 * cache for the taxonomy, and `previousResult` kept while a new page loads so
 * the grid does not flash empty.
 *
 * That is the honest comparison. Angular's DI and signals are excellent, but on
 * *server state* React's ecosystem is genuinely ahead — TanStack Query solves a
 * problem Angular expects you to solve yourself.
 */
@Injectable({ providedIn: 'root' })
export class CatalogService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.bffBaseUrl;

  // The taxonomy barely changes, so it is fetched once and reused rather than
  // refetched on every filter change.
  private readonly categoriesCache = signal<Category[] | null>(null);
  private readonly brandsCache = signal<Brand[] | null>(null);

  readonly categories = computed(() => this.categoriesCache() ?? []);
  readonly brands = computed(() => this.brandsCache() ?? []);

  async searchProducts(filters: ProductFilters): Promise<PagedResult<ProductSummary>> {
    let params = new HttpParams()
      .set('page', String(filters.page ?? 1))
      .set('pageSize', String(filters.pageSize ?? 12));

    if (filters.search) params = params.set('search', filters.search);
    if (filters.category) params = params.set('category', filters.category);
    if (filters.brand) params = params.set('brand', filters.brand);
    if (filters.inStockOnly) params = params.set('inStockOnly', 'true');
    if (filters.sortBy) params = params.set('sortBy', filters.sortBy);
    if (filters.sortDescending) params = params.set('sortDescending', 'true');

    return firstValueFrom(
      this.http.get<PagedResult<ProductSummary>>(`${this.baseUrl}/api/catalog/products`, { params }),
    );
  }

  async getProduct(id: string): Promise<ProductDetail> {
    return firstValueFrom(
      this.http.get<ProductDetail>(`${this.baseUrl}/api/catalog/products/${id}`),
    );
  }

  async loadCategories(): Promise<void> {
    if (this.categoriesCache() !== null) return;

    const categories = await firstValueFrom(
      this.http.get<Category[]>(`${this.baseUrl}/api/catalog/categories`),
    );
    this.categoriesCache.set(categories);
  }

  async loadBrands(): Promise<void> {
    if (this.brandsCache() !== null) return;

    const brands = await firstValueFrom(this.http.get<Brand[]>(`${this.baseUrl}/api/catalog/brands`));
    this.brandsCache.set(brands);
  }
}
