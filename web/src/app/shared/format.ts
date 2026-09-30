import { activeLocale } from '../core/i18n/language';
import { fromDateOnly } from './date-only';

// Display-only formatters that follow the active language (en → en-US, pl → pl-PL). Each reads the
// `activeLocale` signal, so a template or computed() calling one re-renders on a language switch.
// The server computes every monetary value (angular.md) — these never do arithmetic.

export function formatMoney(amount: number | string, currency = 'PLN'): string {
  try {
    return new Intl.NumberFormat(activeLocale(), { style: 'currency', currency }).format(
      Number(amount),
    );
  } catch {
    return `${amount} ${currency}`;
  }
}

export function formatQuantity(quantity: number | string): string {
  return new Intl.NumberFormat(activeLocale(), { maximumFractionDigits: 8 }).format(
    Number(quantity),
  );
}

// An interest rate already expressed in percent (5.25 → "5.25 %"), not a 0–1 fraction.
export function formatPercent(value: number | string): string {
  return `${new Intl.NumberFormat(activeLocale(), { maximumFractionDigits: 4 }).format(Number(value))} %`;
}

// A "YYYY-MM-DD" DateOnly string is a local calendar day (never parsed as UTC midnight); any other
// string is an ISO timestamp. Nullish renders as nothing.
function toDate(value: string | Date): Date {
  return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)
    ? fromDateOnly(value)
    : new Date(value);
}

// Medium date: "Sep 27, 2026" / "27 wrz 2026".
export function formatDate(value: string | Date | null | undefined): string {
  return value === null || value === undefined || value === ''
    ? ''
    : new Intl.DateTimeFormat(activeLocale(), { dateStyle: 'medium' }).format(toDate(value));
}

// A calendar month: "September 2026" / "wrzesień 2026". Nullish renders as nothing.
export function formatMonth(value: string | Date | null | undefined): string {
  return value === null || value === undefined || value === ''
    ? ''
    : new Intl.DateTimeFormat(activeLocale(), { month: 'long', year: 'numeric' }).format(
        toDate(value),
      );
}

// Medium date and time: "Sep 27, 2026, 8:30:00 PM" / "27 wrz 2026, 20:30:00".
export function formatDateTime(value: string | Date | null | undefined): string {
  return value === null || value === undefined || value === ''
    ? ''
    : new Intl.DateTimeFormat(activeLocale(), { dateStyle: 'medium', timeStyle: 'medium' }).format(
        toDate(value),
      );
}
