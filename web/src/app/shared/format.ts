import { activeLocale } from '../core/i18n/language';
import { fromDateOnly } from './date-only';

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

export function formatPercent(value: number | string): string {
  return `${new Intl.NumberFormat(activeLocale(), { maximumFractionDigits: 4 }).format(Number(value))} %`;
}

function toDate(value: string | Date): Date {
  return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)
    ? fromDateOnly(value)
    : new Date(value);
}

export function formatDate(value: string | Date | null | undefined): string {
  return value === null || value === undefined || value === ''
    ? ''
    : new Intl.DateTimeFormat(activeLocale(), { dateStyle: 'medium' }).format(toDate(value));
}

export function formatMonth(value: string | Date | null | undefined): string {
  return value === null || value === undefined || value === ''
    ? ''
    : new Intl.DateTimeFormat(activeLocale(), { month: 'long', year: 'numeric' }).format(
        toDate(value),
      );
}

export function formatDateTime(value: string | Date | null | undefined): string {
  return value === null || value === undefined || value === ''
    ? ''
    : new Intl.DateTimeFormat(activeLocale(), { dateStyle: 'medium', timeStyle: 'medium' }).format(
        toDate(value),
      );
}
