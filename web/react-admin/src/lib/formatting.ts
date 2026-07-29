/**
 * Locale-aware formatting, written once so React and Angular render identical
 * strings.
 *
 * This is not fussiness. If React uses `Intl.NumberFormat` and Angular uses its
 * `CurrencyPipe`, the two produce subtly different output ("£1,234.50" vs
 * "£1,234.50" — until a locale where they diverge on the space before the
 * symbol). The shared Playwright suite asserts on visible text, so any such
 * difference fails the parity run. One implementation makes that impossible.
 */

/** Money as the API sends it. Never a bare number — see below. */
export interface Money {
  readonly amount: number;
  readonly currency: string;
}

/**
 * Formats money for display.
 *
 * Currency travels *with* the amount rather than being assumed, for the same
 * reason the backend has a `Money` value object: an amount without a currency
 * is not a price, and the bug it causes is silent and expensive.
 */
export function formatMoney(money: Money, locale = 'en-GB'): string {
  return new Intl.NumberFormat(locale, {
    style: 'currency',
    currency: money.currency,
  }).format(money.amount);
}

export function formatNumber(value: number, locale = 'en-GB'): string {
  return new Intl.NumberFormat(locale).format(value);
}

/** A date, without a time — for order dates and similar. */
export function formatDate(value: string | Date, locale = 'en-GB'): string {
  const date = typeof value === 'string' ? new Date(value) : value;
  return new Intl.DateTimeFormat(locale, { dateStyle: 'medium' }).format(date);
}

/** Date and time — for audit entries and status timelines. */
export function formatDateTime(value: string | Date, locale = 'en-GB'): string {
  const date = typeof value === 'string' ? new Date(value) : value;
  return new Intl.DateTimeFormat(locale, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date);
}

/**
 * "3 minutes ago", "in 2 days".
 *
 * Uses `Intl.RelativeTimeFormat` rather than a hand-rolled table, so it
 * localises and pluralises correctly without us owning that logic.
 */
export function formatRelativeTime(value: string | Date, locale = 'en-GB'): string {
  const date = typeof value === 'string' ? new Date(value) : value;
  const seconds = Math.round((date.getTime() - Date.now()) / 1000);

  const units: readonly [Intl.RelativeTimeFormatUnit, number][] = [
    ['year', 60 * 60 * 24 * 365],
    ['month', 60 * 60 * 24 * 30],
    ['week', 60 * 60 * 24 * 7],
    ['day', 60 * 60 * 24],
    ['hour', 60 * 60],
    ['minute', 60],
    ['second', 1],
  ];

  const formatter = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });

  for (const [unit, secondsInUnit] of units) {
    if (Math.abs(seconds) >= secondsInUnit || unit === 'second') {
      return formatter.format(Math.round(seconds / secondsInUnit), unit);
    }
  }

  return formatter.format(0, 'second');
}

/** A postal address as stored by User Profile and snapshotted onto an order. */
export interface Address {
  readonly line1: string;
  readonly line2?: string;
  readonly city: string;
  readonly postcode: string;
  readonly country: string;
}

/** Single line, for tables and summaries. */
export function formatAddress(address: Address): string {
  return [address.line1, address.line2, address.city, address.postcode, address.country]
    .filter((part): part is string => Boolean(part && part.trim()))
    .join(', ');
}

/** Multi-line, for confirmation screens. */
export function formatAddressLines(address: Address): readonly string[] {
  return [address.line1, address.line2, address.city, address.postcode, address.country].filter(
    (part): part is string => Boolean(part && part.trim()),
  );
}

/**
 * Truncates to a maximum length, appending an ellipsis.
 *
 * Uses the single ellipsis character rather than three dots: screen readers
 * announce "…" as "ellipsis" but read "..." as three separate full stops.
 */
export function truncate(value: string, maxLength: number): string {
  return value.length <= maxLength ? value : `${value.slice(0, maxLength - 1).trimEnd()}…`;
}
