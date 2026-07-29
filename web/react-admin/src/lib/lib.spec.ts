import { describe, expect, it } from 'vitest';

import {
  formatAddress,
  formatAddressLines,
  formatMoney,
  formatNumber,
  truncate,
} from './formatting';
import {
  Permissions,
  Roles,
  hasAllPermissions,
  hasAnyPermission,
  hasPermission,
  hasRole,
  type AuthenticatedUser,
} from './permissions';

/**
 * Unit tests for this app's own copy of the supporting modules.
 *
 * ---
 * **Why this file is duplicated in the other three applications.**
 *
 * There are now FOUR copies of `permissions` and `formatting` - one per application. Since
 * [ADR-0018](../../../../docs/adr/0018-self-contained-frontends.md) each app
 * owns its own `permissions` and `formatting`, so a fix applied to one copy can
 * silently miss the other three. The [e2e suite](../../../../tests/e2e/) catches drift
 * that is *visible on screen*; this catches drift that is not — a currency
 * separator, a permission helper's treatment of an empty list, an off-by-one in
 * `truncate`.
 *
 * The assertions below are **identical in all four apps on purpose**. If one copy changes
 * behaviour, exactly one of the four suites goes red, and the diff points straight at the
 * divergence.
 */

const customer: AuthenticatedUser = {
  id: '0192f4c1-0000-7000-8000-000000000001',
  username: 'customer',
  displayName: 'Casey Customer',
  email: 'customer@example.com',
  roles: [Roles.Customer],
  permissions: [
    Permissions.Catalog.Read,
    Permissions.Order.ReadOwn,
    Permissions.Order.Write,
    Permissions.Profile.ReadOwn,
    Permissions.Profile.WriteOwn,
  ],
};

describe('permissions', () => {
  it('grants a permission the user holds', () => {
    expect(hasPermission(customer, Permissions.Catalog.Read)).toBe(true);
  });

  it('denies a permission the user does not hold', () => {
    expect(hasPermission(customer, Permissions.Catalog.Write)).toBe(false);
  });

  // `order:read` and `order:read:own` are different permissions with different
  // scopes. A prefix or `startsWith` implementation would wrongly conflate them
  // and let a customer read every order in the shop.
  it('does not treat a broader permission as implied by the :own variant', () => {
    expect(hasPermission(customer, Permissions.Order.ReadOwn)).toBe(true);
    expect(hasPermission(customer, Permissions.Order.Read)).toBe(false);
  });

  // A signed-out user is `null`, not an object with an empty array. Every helper
  // has to survive that or every guarded component needs its own null check.
  it('denies everything when nobody is signed in', () => {
    expect(hasPermission(null, Permissions.Catalog.Read)).toBe(false);
    expect(hasAnyPermission(null, Permissions.Catalog.Read)).toBe(false);
    expect(hasAllPermissions(null, Permissions.Catalog.Read)).toBe(false);
    expect(hasRole(null, Roles.Customer)).toBe(false);
  });

  it('requires every permission for hasAllPermissions', () => {
    expect(hasAllPermissions(customer, Permissions.Catalog.Read, Permissions.Profile.ReadOwn)).toBe(
      true,
    );
    expect(hasAllPermissions(customer, Permissions.Catalog.Read, Permissions.Catalog.Write)).toBe(
      false,
    );
  });

  it('requires only one permission for hasAnyPermission', () => {
    expect(hasAnyPermission(customer, Permissions.Catalog.Write, Permissions.Profile.ReadOwn)).toBe(
      true,
    );
    expect(hasAnyPermission(customer, Permissions.Catalog.Write)).toBe(false);
  });

  // The empty-list cases follow the mathematical convention rather than being a
  // judgement call: "all of nothing" is vacuously true, "any of nothing" is
  // false. Getting these backwards would make an unguarded route either always
  // open or always shut.
  it('treats no arguments as vacuously true for all, and false for any', () => {
    expect(hasAllPermissions(customer)).toBe(true);
    expect(hasAnyPermission(customer)).toBe(false);
  });

  it('reads roles independently of permissions', () => {
    expect(hasRole(customer, Roles.Customer)).toBe(true);
    expect(hasRole(customer, Roles.Admin)).toBe(false);
  });
});

describe('formatting', () => {
  // Currency travels with the amount. Formatting a bare number and assuming a
  // symbol is the silent, expensive bug this signature exists to prevent.
  it('formats money using the currency carried with the amount', () => {
    expect(formatMoney({ amount: 1234.5, currency: 'GBP' })).toBe('£1,234.50');
    expect(formatMoney({ amount: 1234.5, currency: 'EUR' })).toBe('€1,234.50');
  });

  // Intl separates the amount from a trailing symbol with U+202F (narrow
  // no-break space), not a plain space. Normalising every kind of space to a
  // plain one keeps the assertion readable and stops it breaking when ICU data
  // is updated in a future Node release.
  it('respects the locale for grouping and symbol placement', () => {
    const formatted = formatMoney({ amount: 1234.5, currency: 'EUR' }, 'de-DE');
    expect(formatted.replace(/\s/gu, ' ')).toBe('1.234,50 €');
  });

  it('always shows two decimal places, including for whole amounts', () => {
    expect(formatMoney({ amount: 5, currency: 'GBP' })).toBe('£5.00');
  });

  it('groups thousands in plain numbers', () => {
    expect(formatNumber(1234567)).toBe('1,234,567');
  });

  // An optional second line is the norm, not the exception. Joining blindly
  // produces "12 Rosewood Avenue, , Bristol" on most real addresses.
  it('omits empty address parts rather than leaving double separators', () => {
    expect(
      formatAddress({
        line1: '12 Rosewood Avenue',
        city: 'Bristol',
        postcode: 'BS1 4TP',
        country: 'GB',
      }),
    ).toBe('12 Rosewood Avenue, Bristol, BS1 4TP, GB');
  });

  it('treats a whitespace-only line as absent', () => {
    expect(
      formatAddress({
        line1: '12 Rosewood Avenue',
        line2: '   ',
        city: 'Bristol',
        postcode: 'BS1 4TP',
        country: 'GB',
      }),
    ).toBe('12 Rosewood Avenue, Bristol, BS1 4TP, GB');
  });

  it('returns the same parts as separate lines for confirmation screens', () => {
    expect(
      formatAddressLines({
        line1: '12 Rosewood Avenue',
        line2: 'Flat 4',
        city: 'Bristol',
        postcode: 'BS1 4TP',
        country: 'GB',
      }),
    ).toEqual(['12 Rosewood Avenue', 'Flat 4', 'Bristol', 'BS1 4TP', 'GB']);
  });

  it('leaves a string shorter than the limit untouched', () => {
    expect(truncate('Short', 10)).toBe('Short');
  });

  it('leaves a string exactly at the limit untouched', () => {
    expect(truncate('1234567890', 10)).toBe('1234567890');
  });

  // The result must never exceed maxLength - a truncation that overflows its own
  // budget defeats the point, and the ellipsis is one character of it.
  it('never returns more characters than the limit allows', () => {
    const result = truncate('The quick brown fox jumps over the lazy dog', 10);
    expect(result.length).toBeLessThanOrEqual(10);
    expect(result).toBe('The quick…');
  });

  // A single "…" is announced as "ellipsis"; three dots are read as three
  // separate full stops.
  it('uses the single ellipsis character, not three dots', () => {
    expect(truncate('abcdefghij', 5)).toContain('…');
    expect(truncate('abcdefghij', 5)).not.toContain('...');
  });

  it('does not leave a dangling space before the ellipsis', () => {
    expect(truncate('abcd efgh', 6)).toBe('abcd…');
  });
});
