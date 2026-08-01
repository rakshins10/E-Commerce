import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useAuth } from 'react-oidc-context';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { createAdminApi, type AdminProduct } from '../lib/admin-api';
import { formatMoney } from '../lib/formatting';
import { DataTable, type Column } from '../components/DataTable';
import { useCurrentUser } from '../auth/useCurrentUser';
import { Permissions } from '../lib/permissions';

function useApi() {
  const auth = useAuth();

  return useMemo(
    () => createAdminApi(() => auth.user?.access_token ?? null),
    [auth.user?.access_token],
  );
}

/**
 * The catalogue.
 *
 * ---
 * **Three permissions are visible in this one screen**, which is the point of it as a teaching
 * example. A `catalog-manager` sees everything; somebody holding only `catalog:write` can add and edit
 * but the price field is read-only and the withdraw button is absent. The server enforces each
 * independently.
 */
export function CatalogPage() {
  const api = useApi();
  const queryClient = useQueryClient();
  const { can } = useCurrentUser();
  const [search, setSearch] = useState('');
  const [showWithdrawn, setShowWithdrawn] = useState(false);

  const productsQuery = useQuery({
    queryKey: ['admin-products', search],
    queryFn: () => api.getProducts(search || undefined),
    enabled: !showWithdrawn,
  });

  const withdrawnQuery = useQuery({
    queryKey: ['admin-products-withdrawn'],
    queryFn: () => api.getWithdrawnProducts(),
    enabled: showWithdrawn,
  });

  const withdraw = useMutation({
    mutationFn: (id: string) => api.withdrawProduct(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['admin-products'] });
      void queryClient.invalidateQueries({ queryKey: ['admin-products-withdrawn'] });
    },
  });

  const restore = useMutation({
    mutationFn: (id: string) => api.restoreProduct(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['admin-products'] });
      void queryClient.invalidateQueries({ queryKey: ['admin-products-withdrawn'] });
    },
  });

  const query = showWithdrawn ? withdrawnQuery : productsQuery;
  const rows: readonly AdminProduct[] = showWithdrawn
    ? (withdrawnQuery.data ?? [])
    : (productsQuery.data?.items ?? []);

  const columns: Column<AdminProduct>[] = [
    {
      header: 'SKU',
      isRowHeader: true,
      render: (row) =>
        can(Permissions.Catalog.Write) ? (
          <Link to={`/catalog/${row.id}`}>{row.sku}</Link>
        ) : (
          row.sku
        ),
    },
    {
      header: 'Name',
      // A thumbnail beside the name, because a catalogue manager recognises the product long before
      // they finish reading the SKU. Decorative - the name is right next to it, so a screen reader
      // would only hear the same thing twice.
      render: (row) => (
        <span className="cell-with-thumb">
          <img
            className="thumb"
            src={row.imageUrl ?? '/img/placeholder.svg'}
            alt=""
            aria-hidden="true"
            loading="lazy"
            width={40}
            height={40}
          />
          {row.name}
        </span>
      ),
    },
    { header: 'Category', render: (row) => row.categoryName },
    { header: 'Brand', render: (row) => row.brandName },
    {
      header: 'Price',
      numeric: true,
      render: (row) => formatMoney({ amount: row.price, currency: row.currency }),
    },
    { header: 'Stock', numeric: true, render: (row) => row.stockOnHand },
  ];

  if (can(Permissions.Catalog.Delete)) {
    columns.push({
      header: 'Actions',
      render: (row) =>
        showWithdrawn ? (
          <button
            type="button"
            className="btn btn--secondary"
            disabled={restore.isPending}
            onClick={() => restore.mutate(row.id)}
          >
            <span aria-hidden="true">Restore</span>
            <span className="visually-hidden">Put {row.name} back on sale</span>
          </button>
        ) : (
          <button
            type="button"
            className="btn btn--secondary"
            disabled={withdraw.isPending}
            onClick={() => withdraw.mutate(row.id)}
          >
            {/* "Withdraw", not "Delete". The row survives so historic orders keep working, and
                calling it delete would promise something the system deliberately does not do. */}
            <span aria-hidden="true">Withdraw</span>
            <span className="visually-hidden">Withdraw {row.name} from sale</span>
          </button>
        ),
    });
  }

  return (
    <div className="stack">
      <div className="row">
        <h1 className="page-title">Catalogue</h1>

        {can(Permissions.Catalog.Write) && (
          <Link className="btn btn--primary" to="/catalog/new">
            Add product
          </Link>
        )}
      </div>

      <div className="card stack">
        <div className="field">
          <label htmlFor="product-search">Search</label>
          <input
            id="product-search"
            type="search"
            className="input"
            placeholder="Name, SKU or description"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            disabled={showWithdrawn}
          />
        </div>

        <div className="field field--checkbox">
          <input
            id="show-withdrawn"
            type="checkbox"
            checked={showWithdrawn}
            onChange={(event) => setShowWithdrawn(event.target.checked)}
          />
          {/* Without this the only way back from a withdrawal is the database, because every other
              query filters withdrawn products out. */}
          <label htmlFor="show-withdrawn">Show withdrawn products</label>
        </div>
      </div>

      {query.isPending ? (
        <div className="centred" aria-busy="true" aria-live="polite">
          <p className="lede">Loading the catalogue…</p>
        </div>
      ) : query.isError ? (
        <div className="card stack" role="alert">
          <h2>Could not load the catalogue</h2>
          <p className="muted">{(query.error as Error).message}</p>
          <div>
            <button type="button" className="btn btn--primary" onClick={() => query.refetch()}>
              Try again
            </button>
          </div>
        </div>
      ) : (
        <>
          <p role="status" className="muted">
            {rows.length} {rows.length === 1 ? 'product' : 'products'}
            {showWithdrawn ? ' withdrawn from sale' : ''}
          </p>

          <DataTable
            caption={showWithdrawn ? 'Products withdrawn from sale' : 'Products on sale'}
            rows={rows}
            rowKey={(row) => row.id}
            emptyMessage={
              showWithdrawn ? 'Nothing is withdrawn.' : 'No products match that search.'
            }
            columns={columns}
          />
        </>
      )}

      {(withdraw.isError || restore.isError) && (
        <p className="muted" role="alert">
          {((withdraw.error ?? restore.error) as Error).message}
        </p>
      )}
    </div>
  );
}

const EMPTY = {
  sku: '',
  name: '',
  description: '',
  price: '0',
  categoryId: '',
  brandId: '',
  imageUrl: '',
};

/**
 * Add or edit a product.
 *
 * ---
 * **Price is handled separately when editing.** Creating a product sets its price in the same request —
 * there is nothing to override yet. Changing an existing price is a distinct action with a distinct
 * permission, so it gets its own field and its own button, and somebody with `catalog:write` but not
 * `price:override` sees the field disabled rather than the whole form.
 */
export function ProductEditPage() {
  const { id } = useParams<{ id: string }>();
  const isNew = id === 'new';
  const api = useApi();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { can } = useCurrentUser();

  /**
   * The form, as one object.
   *
   * Every handler below uses the FUNCTIONAL updater — `setForm((current) => …)` — never
   * `setForm({ ...form, x })`. The second form captures `form` from the render it was created in, so
   * a value written by an effect between two keystrokes is silently discarded by the next one.
   *
   * That is exactly how this page first failed: the effect below picked a default category, Playwright
   * filled four fields in quick succession, and one of those spreads carried a stale `form` with
   * `categoryId: ''`. The server rejected it with "The JSON value could not be converted to
   * System.Guid", which names the symptom and not the cause.
   *
   * Angular's version has no equivalent hazard: a `FormGroup` is one mutable object that
   * `patchValue` writes into, so there is no snapshot to go stale. A real point for reactive forms.
   */
  const [form, setForm] = useState(EMPTY);
  const [price, setPrice] = useState('0');
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState<string | null>(null);

  const taxonomyQuery = useQuery({
    queryKey: ['taxonomy'],
    queryFn: async () => ({
      categories: await api.getCategories(),
      brands: await api.getBrands(),
    }),
    staleTime: 5 * 60 * 1000,
  });

  const productQuery = useQuery({
    queryKey: ['admin-product', id],
    queryFn: () => api.getProduct(id!),
    enabled: !isNew && Boolean(id),
  });

  // Fill the form once the product and the taxonomy have both arrived. Guarded on `form.name === ''`
  // so a background refetch cannot overwrite what the user is typing.
  useEffect(() => {
    const product = productQuery.data;
    const taxonomy = taxonomyQuery.data;

    if (!product || !taxonomy || form.name !== '') return;

    setForm({
      sku: product.sku,
      name: product.name,
      description: product.description,
      price: String(product.price),
      categoryId: taxonomy.categories.find((c) => c.slug === product.categorySlug)?.id ?? '',
      brandId: taxonomy.brands.find((b) => b.slug === product.brandSlug)?.id ?? '',
      imageUrl: product.imageUrl ?? '',
    });

    setPrice(String(product.price));
  }, [productQuery.data, taxonomyQuery.data, form.name]);

  // Defaults for a new product, so the selects are not empty and the first save cannot fail on a
  // missing category.
  useEffect(() => {
    const taxonomy = taxonomyQuery.data;

    if (!isNew || !taxonomy || form.categoryId !== '') return;

    setForm((current) => ({
      ...current,
      categoryId: taxonomy.categories[0]?.id ?? '',
      brandId: taxonomy.brands[0]?.id ?? '',
    }));
  }, [taxonomyQuery.data, isNew, form.categoryId]);

  const save = useMutation({
    mutationFn: () =>
      isNew
        ? api.createProduct({
            sku: form.sku,
            name: form.name,
            description: form.description,
            price: Number(form.price),
            currency: 'GBP',
            categoryId: form.categoryId,
            brandId: form.brandId,
            imageUrl: form.imageUrl || null,
          })
        : api.updateProduct(id!, {
            name: form.name,
            description: form.description,
            categoryId: form.categoryId,
            brandId: form.brandId,
            imageUrl: form.imageUrl || null,
          }),
    onSuccess: (product) => {
      void queryClient.invalidateQueries({ queryKey: ['admin-products'] });

      if (isNew) {
        void navigate(`/catalog/${product.id}`, { replace: true });
      }

      setSaved('Product saved');
    },
    onError: (mutationError: Error) => setError(mutationError.message),
  });

  const repriceMutation = useMutation({
    mutationFn: () => api.changePrice(id!, Number(price)),
    onSuccess: (product) => {
      queryClient.setQueryData(['admin-product', id], product);
      void queryClient.invalidateQueries({ queryKey: ['admin-products'] });
      setSaved(`Price changed to ${formatMoney({ amount: product.price, currency: product.currency })}`);
    },
    onError: (mutationError: Error) => setError(mutationError.message),
  });

  if (!isNew && productQuery.isPending) {
    return (
      <div className="centred" aria-busy="true" aria-live="polite">
        <p className="lede">Loading the product…</p>
      </div>
    );
  }

  const taxonomy = taxonomyQuery.data;
  // Requires the taxonomy too, not just the text fields. Belt and braces after the stale-closure bug
  // above: even if a default is somehow lost, the form cannot post an empty Guid.
  const canSubmit =
    form.name.trim() !== '' &&
    form.categoryId !== '' &&
    form.brandId !== '' &&
    (!isNew || form.sku.trim() !== '');

  return (
    <div className="stack">
      <h1 className="page-title">{isNew ? 'Add a product' : form.name || 'Edit product'}</h1>

      {error && (
        <div className="card" role="alert">
          <p className="muted">{error}</p>
        </div>
      )}

      {saved && (
        <div className="card" role="status">
          <p className="muted">{saved}</p>
        </div>
      )}

      <form
        className="card stack"
        onSubmit={(event) => {
          event.preventDefault();
          setError(null);
          setSaved(null);
          save.mutate();
        }}
      >
        <div className="field">
          <label htmlFor="sku">SKU</label>
          <input
            id="sku"
            className="input"
            required
            maxLength={64}
            value={form.sku}
            // Immutable after creation. It is what the warehouse picks by and what historic order
            // lines record, so renaming one silently decouples an order from what was shipped.
            disabled={!isNew}
            onChange={(event) => setForm((current) => ({ ...current, sku: event.target.value }))}
          />
          {!isNew && (
            <p className="muted small">
              A SKU cannot be changed — historic orders reference it. Withdraw this product and add a
              new one instead.
            </p>
          )}
        </div>

        <div className="field">
          <label htmlFor="name">Name</label>
          <input
            id="name"
            className="input"
            required
            maxLength={200}
            value={form.name}
            onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))}
          />
        </div>

        <div className="field">
          <label htmlFor="description">Description</label>
          <textarea
            id="description"
            className="input"
            rows={3}
            value={form.description}
            onChange={(event) => setForm((current) => ({ ...current, description: event.target.value }))}
          />
        </div>

        {/* --- Image URL -------------------------------------------------------------------
            This field was missing, and its absence cost a product its picture.

            `PUT /products/{id}` replaces the whole resource, so a field the form does not send is a
            field the server sets to NULL. React happened to survive it by round-tripping `imageUrl`
            through component state; Angular did not track it at all, so the shared "a product can be
            edited" spec wiped the artwork off NW-TS-001 every time it ran against the Angular admin.

            The form is now the fix AND the evidence: a value you can see is a value you notice
            disappearing. */}
        <div className="field">
          <label htmlFor="imageUrl">Image URL</label>
          <input
            id="imageUrl"
            className="input"
            maxLength={500}
            placeholder="/img/tshirt-classic.svg"
            value={form.imageUrl}
            onChange={(event) => setForm((current) => ({ ...current, imageUrl: event.target.value }))}
          />
          <p className="muted small">
            A path served by the storefront, such as <code>/img/mug-ceramic.svg</code>. Leave it empty
            and the shop shows a placeholder.
          </p>

          {form.imageUrl && (
            <img
              className="thumb"
              src={form.imageUrl}
              alt=""
              aria-hidden="true"
              width={40}
              height={40}
            />
          )}
        </div>

        <div className="field">
          <label htmlFor="category">Category</label>
          <select
            id="category"
            className="input"
            value={form.categoryId}
            onChange={(event) => setForm((current) => ({ ...current, categoryId: event.target.value }))}
          >
            {taxonomy?.categories.map((category) => (
              <option key={category.id} value={category.id}>
                {category.name}
              </option>
            ))}
          </select>
        </div>

        <div className="field">
          <label htmlFor="brand">Brand</label>
          <select
            id="brand"
            className="input"
            value={form.brandId}
            onChange={(event) => setForm((current) => ({ ...current, brandId: event.target.value }))}
          >
            {taxonomy?.brands.map((brand) => (
              <option key={brand.id} value={brand.id}>
                {brand.name}
              </option>
            ))}
          </select>
        </div>

        {isNew && (
          <div className="field">
            <label htmlFor="price">Price</label>
            <input
              id="price"
              type="number"
              step="0.01"
              min="0"
              className="input input--narrow"
              value={form.price}
              onChange={(event) => setForm((current) => ({ ...current, price: event.target.value }))}
            />
          </div>
        )}

        <div className="row">
          <button
            type="submit"
            className="btn btn--primary"
            disabled={!canSubmit || save.isPending}
          >
            {save.isPending ? 'Saving…' : isNew ? 'Create product' : 'Save changes'}
          </button>

          <Link className="btn btn--secondary" to="/catalog">
            Back to the catalogue
          </Link>
        </div>
      </form>

      {!isNew && (
        <section className="card stack" aria-labelledby="price-heading">
          <h2 id="price-heading">Price</h2>

          {/* A separate form with its own button, because it needs a separate permission. Somebody
              who can fix a typo should not thereby be able to reprice the shop. */}
          <p className="muted small">
            Changing a price needs the <code>price:override</code> permission, separately from editing
            the product.
          </p>

          <form
            className="row"
            onSubmit={(event) => {
              event.preventDefault();
              setError(null);
              setSaved(null);
              repriceMutation.mutate();
            }}
          >
            <div className="field">
              <label htmlFor="new-price">New price</label>
              <input
                id="new-price"
                type="number"
                step="0.01"
                min="0"
                className="input input--narrow"
                value={price}
                disabled={!can(Permissions.Catalog.PriceOverride)}
                onChange={(event) => setPrice(event.target.value)}
              />
            </div>

            {can(Permissions.Catalog.PriceOverride) && (
              <button
                type="submit"
                className="btn btn--primary"
                disabled={repriceMutation.isPending}
              >
                Change price
              </button>
            )}
          </form>
        </section>
      )}
    </div>
  );
}
