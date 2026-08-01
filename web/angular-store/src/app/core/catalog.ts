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
  /** The TOTAL across variants. A card says "In stock"; a product page says which size. */
  readonly stockOnHand: number;
  readonly audience: string;
}

export interface ProductDetail extends ProductSummary {
  readonly description: string;
  readonly variants: readonly ProductVariant[];
}

/**
 * One sellable size-and-colour of a product.
 *
 * The SKU here is the sellable one — what goes in the basket, what the warehouse picks, and what
 * Inventory holds stock against. `ProductSummary.sku` is the style code
 * ([ADR-0020](../../../docs/adr/0020-product-variants.md)).
 */
export interface ProductVariant {
  readonly id: string;
  readonly productId: string;
  readonly sku: string;
  /** Null when the product has no size axis — a mug does not come in a size. */
  readonly size: string | null;
  readonly colourName: string | null;
  readonly colourHex: string | null;
  readonly stockOnHand: number;
}

/** One value a shopper can filter by, and how many products carry it. */
export interface FacetValue {
  readonly value: string;
  readonly hex: string | null;
  readonly productCount: number;
}

export interface Facets {
  readonly audiences: readonly FacetValue[];
  readonly sizes: readonly FacetValue[];
  readonly colours: readonly FacetValue[];
}

/**
 * The distinct sizes offered, in the order the server returned them.
 *
 * <b>Never sorted here.</b> The server orders by `array_position(ARRAY['S','M','L','XL'], size)`, because
 * alphabetical puts L before M before S before XL — which reads as a bug on every product page in the
 * shop. Re-sorting client-side would undo that.
 */
export function sizesOf(variants: readonly ProductVariant[]): readonly string[] {
  const seen = new Set<string>();
  const sizes: string[] = [];

  for (const variant of variants) {
    if (variant.size && !seen.has(variant.size)) {
      seen.add(variant.size);
      sizes.push(variant.size);
    }
  }

  return sizes;
}

/** The distinct colours offered, first occurrence wins so the swatch comes with it. */
export function coloursOf(
  variants: readonly ProductVariant[],
): readonly { name: string; hex: string | null }[] {
  const seen = new Set<string>();
  const colours: { name: string; hex: string | null }[] = [];

  for (const variant of variants) {
    if (variant.colourName && !seen.has(variant.colourName)) {
      seen.add(variant.colourName);
      colours.push({ name: variant.colourName, hex: variant.colourHex });
    }
  }

  return colours;
}

/**
 * Finds the variant for a chosen size and colour.
 *
 * Both arguments are matched, including when one is null — a product with no size axis has variants whose
 * size IS null, so `null` is a real value to match rather than "any". Treating it as a wildcard would let
 * a mug's White variant satisfy a request for a size that does not exist.
 */
export function findVariant(
  variants: readonly ProductVariant[],
  size: string | null,
  colour: string | null,
): ProductVariant | undefined {
  return variants.find((variant) => variant.size === size && variant.colourName === colour);
}

/**
 * Whether any variant in the given size can be bought.
 *
 * Used to strike out a sold-out size in the picker. A size with stock in Navy but none in Black is still
 * offered — the colour picker then shows which combination is unavailable.
 */
export function sizeHasStock(variants: readonly ProductVariant[], size: string): boolean {
  return variants.some((variant) => variant.size === size && variant.stockOnHand > 0);
}

export function colourHasStock(variants: readonly ProductVariant[], colour: string): boolean {
  return variants.some((variant) => variant.colourName === colour && variant.stockOnHand > 0);
}


export interface Category {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly parentSlug: string | null;
  readonly productCount: number;
}

/** A top-level category with the categories that live inside it. */
export interface Department extends Category {
  readonly children: readonly Category[];
}

/**
 * Turns the flat category list into departments.
 *
 * The API returns one row per category with a `parentSlug`, because that is what the table holds and
 * a client that wants a flat list should not have to unpick a tree. Every screen that *displays* the
 * taxonomy wants it grouped, though, so the grouping happens once here rather than three times in JSX.
 *
 * Ordering is preserved from the server (`ORDER BY parent.slug NULLS FIRST, c.name`), so departments
 * and their children are already alphabetical and this does not sort again.
 *
 * A category whose `parentSlug` names a parent that is not in the list is treated as top-level rather
 * than dropped. Losing a category because its parent was withdrawn would hide products from browsing
 * while the search still returned them, which is the worst of both.
 */
export function groupIntoDepartments(categories: readonly Category[]): readonly Department[] {
  const bySlug = new Map(categories.map((category) => [category.slug, category]));

  const children = new Map<string, Category[]>();
  const roots: Category[] = [];

  for (const category of categories) {
    if (category.parentSlug && bySlug.has(category.parentSlug)) {
      const siblings = children.get(category.parentSlug);
      if (siblings) siblings.push(category);
      else children.set(category.parentSlug, [category]);
    } else {
      roots.push(category);
    }
  }

  return roots.map((root) => ({ ...root, children: children.get(root.slug) ?? [] }));
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
  readonly audience?: string;
  readonly size?: string;
  readonly colour?: string;
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
  private readonly facetsCache = signal<Facets | null>(null);

  readonly categories = computed(() => this.categoriesCache() ?? []);
  readonly brands = computed(() => this.brandsCache() ?? []);
  readonly facets = computed(() => this.facetsCache());

  async searchProducts(filters: ProductFilters): Promise<PagedResult<ProductSummary>> {
    let params = new HttpParams()
      .set('page', String(filters.page ?? 1))
      .set('pageSize', String(filters.pageSize ?? 12));

    if (filters.search) params = params.set('search', filters.search);
    if (filters.category) params = params.set('category', filters.category);
    if (filters.brand) params = params.set('brand', filters.brand);
    if (filters.inStockOnly) params = params.set('inStockOnly', 'true');
    if (filters.audience) params = params.set('audience', filters.audience);
    if (filters.size) params = params.set('size', filters.size);
    if (filters.colour) params = params.set('colour', filters.colour);
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

  /** Sizes, colours and audiences. Cached like the taxonomy — it changes just as rarely. */
  async loadFacets(): Promise<void> {
    if (this.facetsCache() !== null) return;

    const facets = await firstValueFrom(
      this.http.get<Facets>(`${this.baseUrl}/api/catalog/facets`),
    );
    this.facetsCache.set(facets);
  }
}
