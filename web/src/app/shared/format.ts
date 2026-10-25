import { activeLocale } from '../core/i18n/language';
import { fromDateOnly } from './date-only';

const MINUS = '−';

function withTrueMinus(text: string): string {
  return text.replace(/-/g, MINUS);
}

export function roundsToZero(value: number): boolean {
  return Math.round(Math.abs(value) * 100) === 0;
}

function currencyFormat(currency: string, extra: Intl.NumberFormatOptions = {}): Intl.NumberFormat {
  const options = {
    style: 'currency',
    currency,
    currencyDisplay: currency === 'PLN' ? 'symbol' : 'code',
    useGrouping: 'always',
    ...extra,
  } as unknown as Intl.NumberFormatOptions;
  return new Intl.NumberFormat(activeLocale(), options);
}

export function formatMoney(amount: number | string, currency = 'PLN'): string {
  try {
    const value = Number(amount);
    return withTrueMinus(currencyFormat(currency).format(roundsToZero(value) ? 0 : value));
  } catch {
    return `${amount} ${currency}`;
  }
}

export function currencySuffix(currency: string): string {
  try {
    const part = currencyFormat(currency)
      .formatToParts(0)
      .find((candidate) => candidate.type === 'currency');
    return part?.value ?? currency;
  } catch {
    return currency;
  }
}

export function formatMoneyCompact(
  amount: number | string,
  currency = 'PLN',
  maximumFractionDigits?: number,
): string {
  try {
    return withTrueMinus(
      currencyFormat(currency, {
        currencyDisplay: 'symbol',
        notation: 'compact',
        ...(maximumFractionDigits === undefined
          ? {}
          : { minimumFractionDigits: 0, maximumFractionDigits }),
      }).format(Number(amount)),
    );
  } catch {
    return `${amount} ${currency}`;
  }
}

export function formatAmount(amount: number | string): string {
  return new Intl.NumberFormat(activeLocale(), {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(Number(amount));
}

export function formatRate(rate: number | string): string {
  return new Intl.NumberFormat(activeLocale(), {
    minimumFractionDigits: 4,
    maximumFractionDigits: 4,
  }).format(Number(rate));
}

export function formatQuantity(quantity: number | string): string {
  const value = Number(quantity);
  const options = {
    maximumFractionDigits: 2,
    useGrouping: 'always',
    roundingMode: 'halfExpand',
  } as unknown as Intl.NumberFormatOptions;
  const format = new Intl.NumberFormat(activeLocale(), options);
  if (value !== 0 && Math.abs(value) < 0.005) {
    return value > 0 ? `< ${format.format(0.01)}` : `> ${format.format(-0.01)}`;
  }
  return format.format(value);
}

export function formatPercentNumber(value: number | string): string {
  return new Intl.NumberFormat(activeLocale(), { maximumFractionDigits: 4 }).format(Number(value));
}

export function formatPercent(value: number | string): string {
  return `${formatPercentNumber(value)} %`;
}

export function formatSignedMoney(amount: number | string, currency = 'PLN'): string {
  const value = Number(amount);
  return withTrueMinus(
    currencyFormat(currency, { signDisplay: 'exceptZero' }).format(roundsToZero(value) ? 0 : value),
  );
}

export function formatSignedPoints(value: number | string): string {
  return `${new Intl.NumberFormat(activeLocale(), {
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
    signDisplay: 'exceptZero',
  }).format(Number(value))} pp`;
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

export function formatMonthName(value: string | Date | null | undefined): string {
  return value === null || value === undefined || value === ''
    ? ''
    : new Intl.DateTimeFormat(activeLocale(), { month: 'long' }).format(toDate(value));
}

export function formatDateTime(value: string | Date | null | undefined): string {
  return value === null || value === undefined || value === ''
    ? ''
    : new Intl.DateTimeFormat(activeLocale(), { dateStyle: 'medium', timeStyle: 'medium' }).format(
        toDate(value),
      );
}

export function formatAxisDate(value: Date, unit: 'day' | 'month' | 'year'): string {
  const options: Intl.DateTimeFormatOptions =
    unit === 'day'
      ? { day: 'numeric', month: 'short' }
      : unit === 'month'
        ? { month: 'short', year: 'numeric' }
        : { year: 'numeric' };
  return new Intl.DateTimeFormat(activeLocale(), options).format(value);
}
