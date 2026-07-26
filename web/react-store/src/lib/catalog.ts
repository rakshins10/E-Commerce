/**
 * Catalog API types and calls.
 *
 * Owned by this application — the Angular storefront has its own equivalent in
 * `core/catalog.ts`. See docs/adr/0018-self-contained-frontends.md.
 *
 * These types mirror the DTOs the BFF returns. From a later phase they will be
 * generated from the BFF's OpenAPI document, so a backend contract change
 * breaks the frontend *build* rather than producing a runtime `undefined`.
 */

import { ApiClient } from './api-client';

const baseUrl = import.meta.env.VITE_BFF_URL ?? 'http://localhost:6001';

/**
 * Browsing is anonymous, so no token is attached.
 *
 * Deliberate: a shop nobody can look at without an account is a shop nobody
 * buys from. Authenticated calls arrive with the basket in Phase 6.
 */
export const catalogApi = new ApiClient({
  baseUrl,
  getAccessToken: () => null,
});

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

/** Everything the browse screen can filter and sort by. Mirrors the API's query string. */
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

export function searchProducts(
  filters: ProductFilters,
  signal?: AbortSignal,
): Promise<PagedResult<ProductSummary>> {
  return catalogApi.get<PagedResult<ProductSummary>>('/api/catalog/products', {
    signal,
    query: {
      search: filters.search || undefined,
      category: filters.category || undefined,
      brand: filters.brand || undefined,
      inStockOnly: filters.inStockOnly || undefined,
      sortBy: filters.sortBy,
      sortDescending: filters.sortDescending || undefined,
      page: filters.page ?? 1,
      pageSize: filters.pageSize ?? 12,
    },
  });
}

export function getProduct(id: string, signal?: AbortSignal): Promise<ProductDetail> {
  return catalogApi.get<ProductDetail>(`/api/catalog/products/${id}`, { signal });
}

export function getCategories(signal?: AbortSignal): Promise<Category[]> {
  return catalogApi.get<Category[]>('/api/catalog/categories', { signal });
}

export function getBrands(signal?: AbortSignal): Promise<Brand[]> {
  return catalogApi.get<Brand[]>('/api/catalog/brands', { signal });
}

/**
 * Stock presentation, shared by the list and detail screens.
 *
 * **Never colour alone.** Roughly 1 in 12 men has some colour vision
 * deficiency, so a green/red dot conveys nothing to them — WCAG 1.4.1. Every
 * caller renders the `label` text alongside the styling.
 */
export type StockLevel = 'in-stock' | 'low-stock' | 'out-of-stock';

export function stockLevel(stockOnHand: number): { level: StockLevel; label: string } {
  if (stockOnHand <= 0) return { level: 'out-of-stock', label: 'Out of stock' };
  if (stockOnHand <= 5) return { level: 'low-stock', label: `Only ${stockOnHand} left` };
  return { level: 'in-stock', label: 'In stock' };
}
