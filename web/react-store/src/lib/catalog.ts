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

/** Everything the browse screen can filter and sort by. Mirrors the API's query string. */
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
      audience: filters.audience || undefined,
      size: filters.size || undefined,
      colour: filters.colour || undefined,
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

export function getFacets(signal?: AbortSignal): Promise<Facets> {
  return catalogApi.get<Facets>('/api/catalog/facets', { signal });
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
