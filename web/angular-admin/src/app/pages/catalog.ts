import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import { Auth } from '../auth/auth';
import { AdminApi } from '../core/admin-api';
import { formatMoney } from '../core/formatting';
import { Permissions } from '../core/permissions';
import type { AdminBrand, AdminCategory, AdminProduct } from '../core/admin-types';

/**
 * The catalogue.
 *
 * Behaviourally identical to the React `CatalogPage`, so the shared Playwright specs pass against both.
 *
 * ---
 * **Three permissions are visible in this one screen.** A `catalog-manager` sees everything; somebody
 * holding only `catalog:write` can add and edit but the price field is read-only and the withdraw button
 * is absent. The server enforces each independently.
 */
@Component({
  selector: 'app-catalog-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink],
  template: `
    <div class="stack">
      <div class="row">
        <h1 class="page-title">Catalogue</h1>

        @if (auth.can(canWrite)) {
          <a class="btn btn--primary" routerLink="/catalog/new">Add product</a>
        }
      </div>

      <div class="card stack">
        <div class="field">
          <label for="product-search">Search</label>
          <input
            id="product-search"
            type="search"
            class="input"
            placeholder="Name, SKU or description"
            [disabled]="showWithdrawn()"
            [ngModel]="search()"
            (ngModelChange)="onSearch($event)"
          />
        </div>

        <div class="field field--checkbox">
          <input
            id="show-withdrawn"
            type="checkbox"
            [checked]="showWithdrawn()"
            (change)="toggleWithdrawn($event)"
          />
          <!-- Without this the only way back from a withdrawal is the database, because every other
               query filters withdrawn products out. -->
          <label for="show-withdrawn">Show withdrawn products</label>
        </div>
      </div>

      @if (isLoading()) {
        <div class="centred" aria-busy="true" aria-live="polite">
          <p class="lede">Loading the catalogue…</p>
        </div>
      } @else if (error()) {
        <div class="card stack" role="alert">
          <h2>Could not load the catalogue</h2>
          <p class="muted">{{ error() }}</p>
          <div><button type="button" class="btn btn--primary" (click)="reload()">Try again</button></div>
        </div>
      } @else {
        <p role="status" class="muted">
          {{ products().length }} {{ products().length === 1 ? 'product' : 'products' }}{{
            showWithdrawn() ? ' withdrawn from sale' : ''
          }}
        </p>

        @if (products().length === 0) {
          <div class="card">
            <p class="muted">
              {{ showWithdrawn() ? 'Nothing is withdrawn.' : 'No products match that search.' }}
            </p>
          </div>
        } @else {
          <div class="card">
            <table class="table">
              <caption class="visually-hidden">
                {{ showWithdrawn() ? 'Products withdrawn from sale' : 'Products on sale' }}
              </caption>
              <thead>
                <tr>
                  <th scope="col">SKU</th>
                  <th scope="col">Name</th>
                  <th scope="col">Category</th>
                  <th scope="col">Brand</th>
                  <th scope="col" style="text-align: right">Price</th>
                  <th scope="col" style="text-align: right">Stock</th>
                  @if (auth.can(canDelete)) {
                    <th scope="col">Actions</th>
                  }
                </tr>
              </thead>
              <tbody>
                @for (product of products(); track product.id) {
                  <tr>
                    <th scope="row">
                      @if (auth.can(canWrite)) {
                        <a [routerLink]="['/catalog', product.id]">{{ product.sku }}</a>
                      } @else {
                        {{ product.sku }}
                      }
                    </th>
                    <!-- A thumbnail beside the name, because a catalogue manager recognises the
                         product long before they finish reading the SKU. Decorative - the name is
                         right next to it, so a screen reader would only hear the same thing twice. -->
                    <td>
                      <span class="cell-with-thumb">
                        <img
                          class="thumb"
                          [src]="product.imageUrl ?? '/img/placeholder.svg'"
                          alt=""
                          aria-hidden="true"
                          loading="lazy"
                          width="40"
                          height="40"
                        />
                        {{ product.name }}
                      </span>
                    </td>
                    <td>{{ product.categoryName }}</td>
                    <td>{{ product.brandName }}</td>
                    <td style="text-align: right">{{ money(product.price, product.currency) }}</td>
                    <td style="text-align: right">{{ product.stockOnHand }}</td>
                    @if (auth.can(canDelete)) {
                      <td>
                        @if (showWithdrawn()) {
                          <button
                            type="button"
                            class="btn btn--secondary"
                            [disabled]="saving()"
                            (click)="restore(product)"
                          >
                            <span aria-hidden="true">Restore</span>
                            <span class="visually-hidden">Put {{ product.name }} back on sale</span>
                          </button>
                        } @else {
                          <!-- "Withdraw", not "Delete". The row survives so historic orders keep
                               working, and calling it delete would promise something the system
                               deliberately does not do. -->
                          <button
                            type="button"
                            class="btn btn--secondary"
                            [disabled]="saving()"
                            (click)="withdraw(product)"
                          >
                            <span aria-hidden="true">Withdraw</span>
                            <span class="visually-hidden">Withdraw {{ product.name }} from sale</span>
                          </button>
                        }
                      </td>
                    }
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }
    </div>
  `,
})
export class CatalogPage {
  protected readonly auth = inject(Auth);
  private readonly api = inject(AdminApi);

  protected readonly products = signal<readonly AdminProduct[]>([]);
  protected readonly search = signal('');
  protected readonly showWithdrawn = signal(false);
  protected readonly isLoading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly canWrite = Permissions.Catalog.Write;
  protected readonly canDelete = Permissions.Catalog.Delete;
  protected readonly money = (amount: number, currency: string) => formatMoney({ amount, currency });

  constructor() {
    void this.reload();
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    void this.reload();
  }

  protected toggleWithdrawn(event: Event): void {
    this.showWithdrawn.set((event.target as HTMLInputElement).checked);
    void this.reload();
  }

  protected async reload(): Promise<void> {
    this.isLoading.set(true);
    this.error.set(null);

    try {
      this.products.set(
        this.showWithdrawn()
          ? await this.api.getWithdrawnProducts()
          : (await this.api.getProducts(this.search() || undefined)).items,
      );
    } catch (cause) {
      this.error.set(message(cause));
    } finally {
      this.isLoading.set(false);
    }
  }

  protected withdraw(product: AdminProduct): void {
    void this.run(() => this.api.withdrawProduct(product.id));
  }

  protected restore(product: AdminProduct): void {
    void this.run(async () => {
      await this.api.restoreProduct(product.id);
    });
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.saving.set(true);
    this.error.set(null);

    try {
      await action();
      await this.reload();
    } catch (cause) {
      this.error.set(message(cause));
    } finally {
      this.saving.set(false);
    }
  }
}

/**
 * Add or edit a product.
 *
 * ---
 * **Price is handled separately when editing.** Creating a product sets its price in the same request —
 * there is nothing to override yet. Changing an existing price is a distinct action with a distinct
 * permission, so it gets its own field and its own button.
 */
@Component({
  selector: 'app-product-edit-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, FormsModule, RouterLink],
  template: `
    @if (isLoading()) {
      <div class="centred" aria-busy="true" aria-live="polite">
        <p class="lede">Loading the product…</p>
      </div>
    } @else {
      <div class="stack">
        <h1 class="page-title">
          {{ isNew() ? 'Add a product' : form.controls.name.value || 'Edit product' }}
        </h1>

        @if (error()) {
          <div class="card" role="alert"><p class="muted">{{ error() }}</p></div>
        }

        @if (saved()) {
          <div class="card" role="status"><p class="muted">{{ saved() }}</p></div>
        }

        <form class="card stack" [formGroup]="form" (ngSubmit)="save()">
          <div class="field">
            <label for="sku">SKU</label>
            <!-- Immutable after creation: it is what the warehouse picks by and what historic order
                 lines record, so renaming one silently decouples an order from what was shipped. -->
            <input id="sku" class="input" formControlName="sku" />
            @if (!isNew()) {
              <p class="muted small">
                A SKU cannot be changed — historic orders reference it. Withdraw this product and add a
                new one instead.
              </p>
            }
          </div>

          <div class="field">
            <label for="name">Name</label>
            <input id="name" class="input" formControlName="name" />
          </div>

          <div class="field">
            <label for="description">Description</label>
            <textarea id="description" class="input" rows="3" formControlName="description"></textarea>
          </div>

          <div class="field">
            <label for="category">Category</label>
            <select id="category" class="input" formControlName="categoryId">
              @for (category of categories(); track category.id) {
                <option [value]="category.id">{{ category.name }}</option>
              }
            </select>
          </div>

          <div class="field">
            <label for="brand">Brand</label>
            <select id="brand" class="input" formControlName="brandId">
              @for (brand of brands(); track brand.id) {
                <option [value]="brand.id">{{ brand.name }}</option>
              }
            </select>
          </div>

          @if (isNew()) {
            <div class="field">
              <label for="price">Price</label>
              <input
                id="price"
                type="number"
                step="0.01"
                min="0"
                class="input input--narrow"
                formControlName="price"
              />
            </div>
          }

          <div class="row">
            <button type="submit" class="btn btn--primary" [disabled]="form.invalid || saving()">
              {{ saving() ? 'Saving…' : isNew() ? 'Create product' : 'Save changes' }}
            </button>
            <a class="btn btn--secondary" routerLink="/catalog">Back to the catalogue</a>
          </div>
        </form>

        @if (!isNew()) {
          <section class="card stack" aria-labelledby="price-heading">
            <h2 id="price-heading">Price</h2>

            <!-- A separate form with its own button, because it needs a separate permission. Somebody
                 who can fix a typo should not thereby be able to reprice the shop. -->
            <p class="muted small">
              Changing a price needs the <code>price:override</code> permission, separately from editing
              the product.
            </p>

            <form class="row" (ngSubmit)="changePrice()">
              <div class="field">
                <label for="new-price">New price</label>
                <input
                  id="new-price"
                  type="number"
                  step="0.01"
                  min="0"
                  name="newPrice"
                  class="input input--narrow"
                  [disabled]="!auth.can(canOverridePrice)"
                  [ngModel]="price()"
                  (ngModelChange)="price.set($event)"
                />
              </div>

              @if (auth.can(canOverridePrice)) {
                <button type="submit" class="btn btn--primary" [disabled]="saving()">
                  Change price
                </button>
              }
            </form>
          </section>
        }
      </div>
    }
  `,
})
export class ProductEditPage {
  readonly id = input.required<string>();

  protected readonly auth = inject(Auth);
  private readonly api = inject(AdminApi);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  protected readonly categories = signal<readonly AdminCategory[]>([]);
  protected readonly brands = signal<readonly AdminBrand[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly saved = signal<string | null>(null);
  protected readonly price = signal(0);

  protected readonly canOverridePrice = Permissions.Catalog.PriceOverride;
  protected readonly isNew = computed(() => this.id() === 'new');

  protected readonly form = this.fb.nonNullable.group({
    sku: ['', [Validators.required, Validators.maxLength(64)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    description: [''],
    price: [0, [Validators.required, Validators.min(0)]],
    categoryId: ['', Validators.required],
    brandId: ['', Validators.required],
  });

  constructor() {
    // A required signal input is not populated until AFTER the constructor - see pages/orders.ts.
    queueMicrotask(() => void this.load());
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      const [categories, brands] = await Promise.all([
        this.api.getCategories(),
        this.api.getBrands(),
      ]);

      this.categories.set(categories);
      this.brands.set(brands);

      if (this.isNew()) {
        // Defaults, so the selects are not empty and the first save cannot fail on a missing category.
        this.form.patchValue({ categoryId: categories[0]?.id ?? '', brandId: brands[0]?.id ?? '' });
      } else {
        const product = await this.api.getProduct(this.id());

        this.form.patchValue({
          sku: product.sku,
          name: product.name,
          description: product.description,
          price: product.price,
          categoryId: categories.find((c) => c.slug === product.categorySlug)?.id ?? '',
          brandId: brands.find((b) => b.slug === product.brandSlug)?.id ?? '',
        });

        // Disabled rather than merely readonly, so the value is excluded from getRawValue()'s
        // validation path and the control cannot be edited by a stray script.
        this.form.controls.sku.disable();
        this.price.set(product.price);
      }
    } catch (cause) {
      this.error.set(message(cause));
    } finally {
      this.isLoading.set(false);
    }
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    this.saved.set(null);

    try {
      const value = this.form.getRawValue();

      if (this.isNew()) {
        const created = await this.api.createProduct({
          sku: value.sku,
          name: value.name,
          description: value.description,
          price: value.price,
          currency: 'GBP',
          categoryId: value.categoryId,
          brandId: value.brandId,
        });

        await this.router.navigate(['/catalog', created.id], { replaceUrl: true });
      } else {
        await this.api.updateProduct(this.id(), {
          name: value.name,
          description: value.description,
          categoryId: value.categoryId,
          brandId: value.brandId,
        });
      }

      this.saved.set('Product saved');
    } catch (cause) {
      this.error.set(message(cause));
    } finally {
      this.saving.set(false);
    }
  }

  protected async changePrice(): Promise<void> {
    this.saving.set(true);
    this.error.set(null);
    this.saved.set(null);

    try {
      const updated = await this.api.changePrice(this.id(), Number(this.price()));
      this.saved.set(
        `Price changed to ${formatMoney({ amount: updated.price, currency: updated.currency })}`,
      );
    } catch (cause) {
      this.error.set(message(cause));
    } finally {
      this.saving.set(false);
    }
  }
}

function message(cause: unknown): string {
  // The API returns RFC 7807 problem details, so the useful sentence is in `detail` rather than the
  // generic status text Angular puts in `message`.
  if (typeof cause === 'object' && cause !== null && 'error' in cause) {
    const problem = (cause as { error?: { detail?: string } }).error;
    if (problem?.detail) return problem.detail;
  }

  return cause instanceof Error ? cause.message : 'Something went wrong.';
}
