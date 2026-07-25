# Design tokens

**Arrives in Phase 3.** This is the single source of truth for colour, spacing, typography, radii, elevation,
and motion across **all five clients** — React storefront, Angular storefront, React admin, Angular admin, and
the React Native app.

## Why tokens rather than a shared component library

The requirement is that the React and Angular apps look and behave identically. There are two ways to get
there:

1. **A cross-framework component library** (Web Components, Stencil, Lit). Deduplicates the view layer too —
   and defeats the entire purpose. If both apps are thin hosts around the same custom elements, neither
   demonstrates idiomatic React or idiomatic Angular. Rejected in
   [ADR-0014](../../docs/adr/0014-react-and-angular-in-lockstep.md).
2. **Shared tokens, per-framework components.** Each app builds its own components using its own idioms, but
   every visual value comes from one place. Identical appearance, genuinely different implementations —
   which is precisely what needs demonstrating.

React Native forces the issue and is the reason the source format is JSON rather than CSS: it has no
stylesheet, no cascade, and no CSS custom properties. A token set defined *as* CSS could never feed it. Defined
as data, it can generate whatever each platform needs.

## Planned shape

```
design-tokens/
  tokens.json          # the source of truth — plain data, no platform assumptions
  build.mjs            # generates the outputs below
  dist/
    tokens.css         # CSS custom properties, with a light and a dark block
    tokens.ts          # typed constants, so a typo is a compile error
    tokens.native.ts   # React Native StyleSheet-compatible values
```

Both storefronts import `tokens.css`; the admin panels do the same; React Native imports `tokens.native.ts`.
**No application defines a colour or a spacing value of its own** — parity-checklist row X1 tracks that, and
a lint rule will reject raw hex values in application stylesheets.

## Principles

- **Semantic names, not literal ones.** `--color-surface-raised`, not `--color-grey-100`. A literal name has
  to be renamed when the value changes, and a dark theme makes "grey-100" actively misleading.
- **Themes are token overrides, not a second stylesheet.** Light and dark differ only in values, so a theme
  switch cannot cause a layout difference.
- **Contrast is validated in the build.** WCAG 2.2 AA requires 4.5:1 for body text and 3:1 for large text and
  UI boundaries; `build.mjs` will fail on a pair that does not meet it. Checking contrast automatically is
  much more reliable than checking it in review.
