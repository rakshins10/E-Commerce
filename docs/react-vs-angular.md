# React vs Angular, feature by feature

> **Related:** [ADR-0014 — lockstep delivery](adr/0014-react-and-angular-in-lockstep.md) ·
> [ADR-0018 — self-contained frontends](adr/0018-self-contained-frontends.md) ·
> [`web/parity-checklist.md`](../web/parity-checklist.md)

Written **as each feature is built**, while the memory of what was awkward is fresh. Retrospective
comparisons turn into generic listicles; notes taken at the moment of the decision do not.

This is an unusually controlled comparison. Both apps implement the same specs, satisfy the same end-to-end
tests, and — since [ADR-0018](adr/0018-self-contained-frontends.md) — each owns its own copy of every
supporting module. So the differences recorded here are genuinely attributable to the framework, not to one
app being a thin wrapper over shared code.

---

## Phase 3 — application shell, routing, theming, OIDC login

**Parity rows:** S1, S2, S4 · **Specs:** [`tests/e2e/specs/auth.spec.ts`](../tests/e2e/specs/auth.spec.ts)
(9 specs, passing against both)

### The one that actually bit: post-login redirect

**Angular needed no code. React did — and the bug was invisible.**

`angular-auth-oidc-client` restores the pre-login route itself once the authorization code is exchanged.
`oidc-client-ts` does not: it exchanges the code and updates its own state, but leaves the browser sitting on
`/auth/callback`.

The failure mode was nasty precisely because it *half* worked. The header rendered the user's name and a
"Sign out" button — authentication had genuinely succeeded — while the page body still read
*"Completing sign-in…"* forever. Nothing errored. Nothing logged. Keycloak's logs showed a clean exchange.

The fix is five lines:

```tsx
// react-store/src/pages/StaticPages.tsx
useEffect(() => {
  if (auth.isAuthenticated) void navigate('/', { replace: true });
}, [auth.isAuthenticated, navigate]);
```

**Point for Angular.** Its library owns more of the flow, and that is exactly the trade Angular makes
generally: more opinions, fewer decisions left to you, fewer places to get it subtly wrong.

**How it was found:** the shared Playwright suite. Its page snapshot showed the header signed in and the body
still loading — which stated the problem more clearly than any amount of staring at the code would have.
That is the parity suite paying for itself on its first run.

### State: hook vs injectable service

| | React | Angular |
|---|---|---|
| Shape | `useCurrentUser()` hook | `Auth` service with signals |
| Reaching it | `<AuthProvider>` at the root, hook in each component | `inject(Auth)` anywhere, no wrapper |
| Recomputation | `useMemo` with an explicit dependency array | `computed()`, dependencies tracked automatically |

```ts
// React — dependencies declared by hand
const user = useMemo(
  () => (auth.user?.access_token ? toAuthenticatedUser(auth.user.access_token) : null),
  [auth.user?.access_token],
);

// Angular — dependencies inferred
readonly user = computed(() => {
  const token = this.accessToken();
  return token ? toAuthenticatedUser(token) : null;
});
```

**Point for Angular**, narrowly. A forgotten dependency in a React array is a stale-closure bug that is easy
to write and hard to see; signals remove the category. The counter-argument is real though: React's array
*documents* the dependency, and Angular's tracking is implicit — you cannot tell what an `effect` depends on
without reading its whole body.

**Point for Angular on DI.** `inject(Auth)` works anywhere with no provider in the component tree. React
needs `<AuthProvider>` mounted at the root and a hook to reach it, and if you forget the provider you get a
runtime error rather than a compile error.

### Routing

| | React | Angular |
|---|---|---|
| Definition | JSX `<Route>` elements | a `Routes` data structure |
| Layout | element wrapping `<Outlet />` | `<router-outlet>` in the root component |
| Active link | `NavLink` sets a class | `routerLinkActive` directive |
| Lazy loading | `React.lazy` — opt-in, easy to forget | `loadComponent` — the default shape of a route |

**Point for Angular on two counts.** `ariaCurrentWhenActive="page"` sets the accessible state and the visual
state from the *same directive*, so they cannot disagree. In React they are separate concerns and it is
entirely possible to style an active link while forgetting `aria-current` — which the shared specs assert on,
so it would have failed CI.

And lazy loading being the *default idiom* of a route definition means you get code splitting without
remembering to ask for it.

**Point for React on readability.** Routes as JSX read as a tree, and the nesting is visually obvious. The
Angular array with `loadComponent` closures is more machinery for the same idea.

### Theming

Behaviourally identical: three states (light / dark / follow-system), one `data-theme` attribute on `<html>`,
same tokens.

```ts
// React
const [theme, setTheme] = useState<Theme>(...);
useEffect(() => { /* apply */ }, [theme]);

// Angular
protected readonly theme = signal<Theme>(...);
constructor() { effect(() => { /* apply */ }); }
```

**Draw.** Genuinely equivalent, and about the same number of lines.

### Templates

React's JSX conditional rendering with `&&` and ternaries gets noisy fast. Angular's `@if` / `@for` block
syntax is easier to scan in a template of any size:

```html
@if (auth.isAuthenticated()) { … } @else { … }
```

versus

```tsx
{isAuthenticated ? (…) : (…)}
```

**Point for Angular** in templates. **Point for React** in that JSX is just JavaScript — no new syntax to
learn, and any expression is available.

### Bundle size

| | Initial JS (gzipped) |
|---|---|
| React storefront | ~59 kB |
| Angular storefront | ~91 kB |

**Point for React**, though the gap narrows on a large application and both are unremarkable for a modern
SPA. Angular ships more framework because it *is* more framework.

### Build strictness

Angular's default `tsconfig` is stricter than Vite's, and it caught a genuine portability bug the React build
did not: `noPropertyAccessFromIndexSignature` rejected `headers.Authorization` on an index-signature type.

**Point for Angular.** Its defaults push you toward safer code with no configuration effort.

### Summary

| Dimension | Winner |
|-----------|--------|
| Getting the OIDC flow right with least code | **Angular** |
| Dependency injection | **Angular** |
| Accessible-by-default routing | **Angular** |
| Lazy loading as the default | **Angular** |
| Template readability at scale | **Angular** |
| Strict-by-default compiler | **Angular** |
| Bundle size | **React** |
| Route definitions as a readable tree | **React** |
| Fewer concepts to learn | **React** |
| Theming | Draw |

Angular looks ahead on this scorecard, and for *this* phase it is. That is a fair reflection of what the
phase consisted of: framework plumbing — auth, routing, DI — which is precisely where Angular's opinions pay
off. The comparison should get more interesting from Phase 4, where data fetching, caching and forms arrive
and TanStack Query enters the picture.

---

## Phase 4 — product browsing

**Parity rows:** S5, S6, S7 · **Specs:** [`tests/e2e/specs/catalog.spec.ts`](../tests/e2e/specs/catalog.spec.ts)
(16 more specs, 25 total, passing against both)

This is the first phase where the comparison gets genuinely interesting, because it is about **server state**
rather than framework plumbing — and the verdict flips.

### Data fetching: React wins clearly

React uses **TanStack Query**. Angular has no framework equivalent, so the same behaviours are hand-built.

| Behaviour | React | Angular |
|---|---|---|
| Cache per filter combination | free (`queryKey`) | hand-rolled signal cache |
| Deduplicate concurrent requests | free | not implemented |
| Keep previous page while loading | `placeholderData: keepPreviousData` | a retained `previousResult` signal |
| Stale time for slow-changing data | `staleTime: 5 * 60_000` | a manual `if (cache() !== null) return` |
| Retry policy per error type | `retry: (n, e) => …` | hand-written try/catch |
| Request cancellation | `queryFn({ signal })` | `withFetch()` + manual plumbing |

```ts
// React — declarative, and the caching is not code you own
const productsQuery = useQuery({
  queryKey: ['products', filters],
  queryFn: ({ signal }) => searchProducts(filters, signal),
  placeholderData: keepPreviousData,
});
```

```ts
// Angular — the same behaviours, written out
protected readonly result = signal<PagedResult<ProductSummary> | null>(null);
protected readonly isLoading = signal(false);
protected readonly error = signal<string | null>(null);

effect(() => { void this.load(this.filters()); });
```

**Point for React, decisively.** TanStack Query solves a problem Angular expects you to solve yourself, and
the hand-rolled version is both more code and less capable — no deduplication, no background refetch, no
cache invalidation story. Angular's `resource()`/`httpResource` are closing this gap, but as of Angular 22
they are not yet a full replacement.

This is the counterweight to Phase 3, where Angular won almost every row. It is worth noticing *why*: Angular
is strong where the **framework** owns the problem (DI, routing, change detection), React is strong where the
**ecosystem** owns it (server state, data fetching). That is a more useful way to hold the comparison than a
scoreboard.

### Reading the URL as state

Both apps keep filters in the URL rather than component state, so a filtered view is shareable and survives a
refresh.

```ts
// React
const [searchParams, setSearchParams] = useSearchParams();
const filters = useMemo(() => ({ search: searchParams.get('search') ?? '', … }), [searchParams]);

// Angular
private readonly queryParams = toSignal(this.route.queryParamMap, { requireSync: true });
protected readonly filters = computed(() => ({ search: this.queryParams().get('search') ?? '', … }));
```

**Narrow point for Angular.** `computed` needs no dependency array, so it cannot go stale. But `toSignal` is a
bridge over an Observable API, and having to reach for it is a reminder that Angular's router has not caught
up with signals yet.

### Route parameters

```ts
// Angular — bound directly from the route by withComponentInputBinding()
readonly id = input.required<string>();

// React
const { id } = useParams<{ id: string }>();
```

**Point for Angular.** A typed, required signal input beats a hook returning `string | undefined`.

### Templates at scale

The browse screen is the first template big enough for this to matter. Angular's `@if` / `@for` blocks stay
readable; React's JSX with nested ternaries and `&&` gets noisy:

```tsx
{result && result.items.length === 0 && (…)}
{result && result.items.length > 0 && (…)}
```

**Point for Angular.**

### Running total

| Dimension | Winner |
|-----------|--------|
| **Server state, caching, request lifecycle** | **React** — and it is not close |
| Route parameter binding | Angular |
| Deriving state without dependency arrays | Angular |
| Template readability at scale | Angular |
| Framework plumbing (DI, routing, auth) | Angular |
| Bundle size | React (59 kB vs 94 kB gzipped) |

The honest summary for an interview: **Angular gives you more, React lets you choose more.** Angular's
batteries cover routing, DI and forms extremely well but stop at server state; React's core is smaller and
the ecosystem fills the gap — better, in the case of TanStack Query, but only because you went and picked it.

---

## Phase 5 — My Account (forms, validation, mutations)

The first screen in this repo that **writes**. Reading data is where React's ecosystem clearly won; writing
it, with validation and eight mutating endpoints, is where Angular claws a large amount back.

Feature-identical in both: contact details, an address book with default-shipping/billing promotion, and a
preferences panel. 9 e2e specs, one suite, both apps.

### Forms: Angular wins clearly, and it is not close

React has no form primitive. Each field is wired by hand:

```tsx
const [addressDraft, setAddressDraft] = useState<SaveAddressRequest>(EMPTY_ADDRESS);

<input
  id="line1"
  value={addressDraft.line1}
  onChange={(e) => setAddressDraft({ ...addressDraft, line1: e.target.value })}
  maxLength={200}
  required
/>
```

Every field costs a `value`, an `onChange`, an object spread and a validation attribute. There is no
`isDirty`, no `touched`, no cross-field validity — you build them or you go without. Six address fields and
seven preference fields make that repetition the dominant shape of the file.

Angular declares the whole form once, typed, with validators as data:

```ts
protected readonly addressForm = this.fb.nonNullable.group({
  label:    ['',   [Validators.required, Validators.maxLength(50)]],
  line1:    ['',   [Validators.required, Validators.maxLength(200)]],
  city:     ['',   [Validators.required, Validators.maxLength(100)]],
  postcode: ['',   [Validators.required, Validators.maxLength(20)]],
  country:  ['GB',  Validators.required],
});
```

```html
<input id="line1" class="input" formControlName="line1" />
<button [disabled]="addressForm.invalid || saving()">Save address</button>
```

`nonNullable.group` gives a **typed** `value` — `addressForm.value.postcode` is `string`, and a typo in a
control name is a compile error. `invalid`, `dirty`, `touched` and `pristine` come free, so disabling the
submit button is one expression rather than a derived boolean somebody has to remember to update.

The honest counterweight: **React Hook Form closes most of this gap.** It was left out on purpose, so the
comparison shows what each framework gives you *in the box*. In production I would add it — and the fact that
you have to is the point. Angular ships forms; React ships a `useState` hook and a healthy npm ecosystem.

| | React (built-in) | Angular (built-in) |
|---|---|---|
| Typed form value | build it | `nonNullable.group` |
| Declarative validators | build it | `Validators.*` |
| `dirty` / `touched` / `invalid` | build it | free |
| Lines for the address form | ~95 | ~40 (`.ts` + template) |

### Mutations: React wins again

Eight endpoints, all returning the full updated profile.

```tsx
const contactMutation = useMutation({
  mutationFn: (body) => saveContact(body),
  onSuccess: (updated) => {
    queryClient.setQueryData(['profile'], updated);   // write the response into the cache
    setBanner('Contact details saved');
  },
});
```

`isPending`, `isError` and `error` come per-mutation, so one form spinning does not disable the others.
`setQueryData` writes the server's response straight into the cache — no refetch, no round trip.

Angular has no mutation primitive, so the service holds one signal that each call replaces:

```ts
async updateContact(displayName: string | null, phoneNumber: string | null): Promise<void> {
  this.profile.set(await firstValueFrom(this.http.put<Profile>(`${this.baseUrl}/contact`, { … })));
}
```

Simpler to read, and correct — because every endpoint returns the *whole* aggregate and there is exactly one
consumer. But the component has to own `saving()`, `error()` and `banner()` signals by hand and share them
across all five operations, which is why the page has a `run()` wrapper:

```ts
private async run(action: () => Promise<void>): Promise<void> {
  this.saving.set(true);
  try { await action(); } catch (e) { this.error.set(message(e)); }
  finally { this.saving.set(false); }
}
```

That wrapper *is* the thing TanStack Query hands you per mutation. Nine lines is not a crisis — but multiply
it by every screen that writes, and the difference compounds.

### Loading and error states

React returns early four times — auth loading, signed-out, query pending, query error — and TypeScript
narrows `profileQuery.data` to non-null after them, so the happy path needs no optional chaining. That is a
genuinely elegant property of early returns in a function component.

Angular's equivalent is `@if` / `@else if` in the template. Slightly more verbose to read, but the states sit
next to the markup they replace rather than 60 lines above it. **Preference, not a winner** — though the
React version does re-declare the same four blocks on every page, where Angular can factor them into a
component with content projection.

### Optimistic updates: neither, on purpose

TanStack Query supports optimistic mutation, and it was not used.

The aggregate's invariants make the *server's* response differ from what you sent: adding your first address
silently makes it the default for both shipping and billing, and removing a default promotes another. An
optimistic client would have to reimplement those rules to guess the outcome — a second copy of the domain
logic, in TypeScript, drifting from the first.

Optimism is right for a basket, where the outcome is obvious and latency is felt on every click. It is wrong
here. **A pattern being available is not a reason to use it**, and knowing which is which is the actual skill.

### The bug this phase found, and where

None of the above. The e2e suite failed on `403 Forbidden` for staff users, which turned out to be a **domain
modelling error** — `profile:read:own` had been granted to `customer` only. Both frontends were correct.

Worth stating plainly: across five phases, **the frontend framework has never been the source of a real
defect**. The costly bugs were EF Core key generation, a Serilog ordering mistake, a container health check
resolving to IPv6, and a permission model that forgot staff are people. Framework choice is the most-debated
and least-consequential decision on the list.

### Running total after Phase 5

| Dimension | Winner |
|-----------|--------|
| **Server state, caching, request lifecycle** | **React** — and it is not close |
| **Mutations — per-call pending/error state** | **React** |
| **Forms — typed, validated, in the box** | **Angular** — and it is not close |
| Route parameter binding | Angular |
| Deriving state without dependency arrays | Angular |
| Template readability at scale | Angular |
| Framework plumbing (DI, routing, auth) | Angular |
| Bundle size | **Angular** — the position reversed, see below |
| Loading/error state ergonomics | Draw |

### Bundle size: React lost the lead it started with

Measured on the production builds at the end of this phase:

| | Raw | Gzipped |
|---|---:|---:|
| React — JS + CSS | 387.5 kB | **112.9 kB** |
| Angular — initial total | 404.8 kB | **96.8 kB** |

React started Phase 3 at 59 kB gzipped against Angular's 94 kB and has nearly doubled; Angular has barely
moved. The cause is not React itself — it is what a React app must add to become a real application.
`@tanstack/react-query`, `oidc-client-ts` + `react-oidc-context` and `react-router` are all runtime
dependencies shipped to the browser. Angular's equivalents are compiled, tree-shaken parts of the framework
that was already being paid for, and its raw output — larger — compresses better because generated code
repeats itself.

The lesson generalises beyond these two frameworks: **"small core" is a starting position, not a steady
state.** Comparing framework hello-worlds measures the first commit; comparing them after routing, auth,
server state and forms measures the application you actually ship. Anyone quoting a framework's baseline
bundle size as a decision criterion is quoting the least durable number available.

The Phase 4 summary still holds and Phase 5 sharpens it: **React's core is smaller and its ecosystem is
better; Angular's box is fuller and more consistent.** A React app that adds TanStack Query *and* React Hook
Form matches Angular on both axes — but that is two library choices, two upgrade paths and two sets of
conventions the team has to agree on, and the app that skips them is measurably worse. Angular's answer is
already in the framework, already typed, and identical in every codebase you will ever join.
