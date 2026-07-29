import { useEffect, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { keepPreviousData, useQuery } from '@tanstack/react-query';

import {
  getBrands,
  getCategories,
  searchProducts,
  stockLevel,
  type ProductFilters,
} from '../lib/catalog';
import { formatMoney } from '../lib/formatting';

/**
 * Product browsing: search, filter, sort, page.
 *
 * ---
 * **Two decisions worth understanding.**
 *
 * 1. **Filters live in the URL**, not in component state. `/products?search=hoodie&category=clothing`
 *    is shareable, bookmarkable, and survives a refresh and the Back button. State held only in
 *    `useState` gives you none of that, and it is the single most common thing missed on a browse
 *    screen.
 *
 * 2. **TanStack Query owns server state.** It caches per filter combination, dedupes, and — via
 *    `keepPreviousData` — keeps the previous page on screen while the next one loads, so paging does
 *    not flash empty. Angular's equivalent is in `pages/products.ts`, built from signals; the
 *    comparison is in docs/react-vs-angular.md.
 */
export function ProductsPage() {
  const [searchParams, setSearchParams] = useSearchParams();

  // Derived from the URL, so there is exactly one source of truth.
  const filters: ProductFilters = useMemo(
    () => ({
      search: searchParams.get('search') ?? '',
      category: searchParams.get('category') ?? '',
      brand: searchParams.get('brand') ?? '',
      inStockOnly: searchParams.get('inStockOnly') === 'true',
      sortBy: (searchParams.get('sortBy') as ProductFilters['sortBy']) ?? 'name',
      sortDescending: searchParams.get('sortDescending') === 'true',
      page: Number(searchParams.get('page') ?? '1'),
      pageSize: 12,
    }),
    [searchParams],
  );

  // The search box needs to feel instant, so it is local state, debounced into
  // the URL below. Writing every keystroke to the URL would spam history and
  // fire a request per character.
  const [searchInput, setSearchInput] = useState(filters.search ?? '');

  useEffect(() => {
    const timer = setTimeout(() => {
      if ((filters.search ?? '') === searchInput) return;
      updateParams({ search: searchInput, page: '1' });
    }, 300);

    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchInput]);

  function updateParams(changes: Record<string, string>) {
    const next = new URLSearchParams(searchParams);

    for (const [key, value] of Object.entries(changes)) {
      if (value) next.set(key, value);
      else next.delete(key);
    }

    setSearchParams(next, { replace: true });
  }

  const productsQuery = useQuery({
    queryKey: ['products', filters],
    queryFn: ({ signal }) => searchProducts(filters, signal),
    // Keeps the current page visible while the next loads, so paging does not
    // flash an empty grid. A small thing that makes the UI feel much steadier.
    placeholderData: keepPreviousData,
  });

  const categoriesQuery = useQuery({
    queryKey: ['categories'],
    queryFn: ({ signal }) => getCategories(signal),
    // The taxonomy barely changes; refetching it on every filter change is waste.
    staleTime: 5 * 60 * 1000,
  });

  const brandsQuery = useQuery({
    queryKey: ['brands'],
    queryFn: ({ signal }) => getBrands(signal),
    staleTime: 5 * 60 * 1000,
  });

  const result = productsQuery.data;
  const hasFilters = Boolean(
    filters.search || filters.category || filters.brand || filters.inStockOnly,
  );

  return (
    <div className="stack">
      <h1 className="page-title">Products</h1>

      {/* --- Filters --- */}
      <section className="card" aria-labelledby="filters-heading">
        <h2 id="filters-heading" className="visually-hidden">
          Filter products
        </h2>

        <div className="filters">
          <div className="field">
            <label htmlFor="search">Search</label>
            <input
              id="search"
              type="search"
              className="input"
              placeholder="Name, SKU or description"
              value={searchInput}
              onChange={(event) => setSearchInput(event.target.value)}
            />
          </div>

          <div className="field">
            <label htmlFor="category">Category</label>
            <select
              id="category"
              className="input"
              value={filters.category}
              onChange={(event) => updateParams({ category: event.target.value, page: '1' })}
            >
              <option value="">All categories</option>
              {categoriesQuery.data?.map((category) => (
                <option key={category.id} value={category.slug}>
                  {category.parentSlug ? `— ${category.name}` : category.name} ({category.productCount})
                </option>
              ))}
            </select>
          </div>

          <div className="field">
            <label htmlFor="brand">Brand</label>
            <select
              id="brand"
              className="input"
              value={filters.brand}
              onChange={(event) => updateParams({ brand: event.target.value, page: '1' })}
            >
              <option value="">All brands</option>
              {brandsQuery.data?.map((brand) => (
                <option key={brand.id} value={brand.slug}>
                  {brand.name} ({brand.productCount})
                </option>
              ))}
            </select>
          </div>

          <div className="field">
            <label htmlFor="sort">Sort by</label>
            <select
              id="sort"
              className="input"
              value={`${filters.sortBy}:${filters.sortDescending ? 'desc' : 'asc'}`}
              onChange={(event) => {
                const [sortBy, direction] = event.target.value.split(':');
                updateParams({
                  sortBy: sortBy!,
                  sortDescending: direction === 'desc' ? 'true' : '',
                  page: '1',
                });
              }}
            >
              <option value="name:asc">Name (A–Z)</option>
              <option value="name:desc">Name (Z–A)</option>
              <option value="price:asc">Price (low to high)</option>
              <option value="price:desc">Price (high to low)</option>
              <option value="brand:asc">Brand</option>
            </select>
          </div>

          <div className="field field--checkbox">
            <input
              id="inStockOnly"
              type="checkbox"
              checked={filters.inStockOnly}
              onChange={(event) =>
                updateParams({ inStockOnly: event.target.checked ? 'true' : '', page: '1' })
              }
            />
            <label htmlFor="inStockOnly">In stock only</label>
          </div>

          {hasFilters && (
            <button
              type="button"
              className="btn btn--secondary"
              onClick={() => {
                setSearchInput('');
                setSearchParams(new URLSearchParams(), { replace: true });
              }}
            >
              Clear filters
            </button>
          )}
        </div>
      </section>

      {/* --- Results ---
          aria-live so a screen-reader user hears the count change after
          filtering. Without it, filtering is silent and appears to do nothing. */}
      <p className="muted" aria-live="polite" role="status">
        {productsQuery.isPending
          ? 'Loading products…'
          : result
            ? `${result.totalCount} product${result.totalCount === 1 ? '' : 's'}`
            : ''}
      </p>

      {productsQuery.isError && (
        <div className="card" role="alert">
          <h2 style={{ marginTop: 0 }}>Could not load products</h2>
          <p className="muted">{(productsQuery.error as Error).message}</p>
          <button type="button" className="btn btn--primary" onClick={() => productsQuery.refetch()}>
            Try again
          </button>
        </div>
      )}

      {productsQuery.isPending && (
        // Skeletons rather than a spinner: the layout does not jump when the
        // real content arrives.
        <div className="grid grid--3" aria-hidden="true">
          {Array.from({ length: 6 }, (_, index) => (
            <div key={index} className="card product-card product-card--skeleton" />
          ))}
        </div>
      )}

      {result && result.items.length === 0 && (
        <div className="card centred-block">
          <h2 style={{ marginTop: 0 }}>No products match</h2>
          <p className="muted">Try a different search or clear the filters.</p>
        </div>
      )}

      {result && result.items.length > 0 && (
        <ul className="grid grid--3 product-grid">
          {result.items.map((product) => {
            const stock = stockLevel(product.stockOnHand);

            return (
              <li key={product.id} className="card product-card">
                <div className="product-media">
                  {/*
                    alt="" and aria-hidden: the illustration carries no information the product name
                    does not already give, and a screen reader announcing "Ceramic Mug illustration"
                    directly above the text "Ceramic Mug" is repetition, not description.

                    Real photography of a real product would deserve a real alt.
                  */}
                  <img
                    className="product-media__img"
                    src={product.imageUrl ?? '/img/placeholder.svg'}
                    alt=""
                    aria-hidden="true"
                    loading="lazy"
                    // The frame is already sized by CSS; these stop the browser reflowing the grid
                    // as each image decodes.
                    width={400}
                    height={300}
                  />
                </div>

                <div className="product-card__body">
                  <p className="product-card__brand">{product.brandName}</p>

                  <h3 className="product-card__name">
                    {/*
                      Only the NAME is the link, so the accessible name is the product and nothing
                      else. The ::after in the stylesheet stretches the hit area to the whole card,
                      which gives a big pointer target without dragging the price and the badge into
                      what a screen reader reads out.
                    */}
                    <Link to={`/products/${product.id}`} className="product-card__link">
                      {product.name}
                    </Link>
                  </h3>
                </div>

                <div className="product-card__footer">
                  <span className="price">
                    {formatMoney({ amount: product.price, currency: product.currency })}
                  </span>
                  <span className={`badge badge--${stock.level}`}>{stock.label}</span>
                </div>
              </li>
            );
          })}
        </ul>
      )}

      {/* --- Paging --- */}
      {result && result.totalPages > 1 && (
        <nav className="pager" aria-label="Pagination">
          <button
            type="button"
            className="btn btn--secondary"
            disabled={!result.hasPrevious}
            onClick={() => updateParams({ page: String(result.page - 1) })}
          >
            Previous
          </button>

          <span className="muted">
            Page {result.page} of {result.totalPages}
          </span>

          <button
            type="button"
            className="btn btn--secondary"
            disabled={!result.hasNext}
            onClick={() => updateParams({ page: String(result.page + 1) })}
          >
            Next
          </button>
        </nav>
      )}
    </div>
  );
}
