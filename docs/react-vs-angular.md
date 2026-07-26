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

_To be written._
