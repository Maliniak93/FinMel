import type { DepositCapitalization, DepositStatus, DepositTermUnit } from '../../api/portfolio';

// Backend enums (Skarbiec.Portfolio.Data.DepositTermUnit / DepositCapitalization,
// Features.Deposits.DepositStatus) serialize as their underlying ints, so the generated client types
// them as bare `number`s — labels live here, in the same declaration order as the C# enums.
export const DEPOSIT_TERM_UNIT = { Days: 0, Months: 1 } as const satisfies Record<
  string,
  DepositTermUnit
>;

export const DEPOSIT_TERM_UNITS: readonly { value: DepositTermUnit; label: string }[] = [
  { value: DEPOSIT_TERM_UNIT.Days, label: 'Days' },
  { value: DEPOSIT_TERM_UNIT.Months, label: 'Months' },
];

export const DEPOSIT_CAPITALIZATIONS: readonly { value: DepositCapitalization; label: string }[] = [
  { value: 0, label: 'At maturity' },
  { value: 1, label: 'Monthly' },
  { value: 2, label: 'Quarterly' },
  { value: 3, label: 'Yearly' },
];

export const DEPOSIT_STATUS = { Active: 0, Due: 1 } as const satisfies Record<
  string,
  DepositStatus
>;

// The same caps AddDepositRequest/UpdateDepositRequest enforce (the API is the source of truth).
export const MAX_TERM_DAYS = 3650;
export const MAX_TERM_MONTHS = 120;

export function maxTermLength(termUnit: DepositTermUnit): number {
  return Number(termUnit) === DEPOSIT_TERM_UNIT.Days ? MAX_TERM_DAYS : MAX_TERM_MONTHS;
}

export function depositStatusLabel(status: DepositStatus): string {
  return Number(status) === DEPOSIT_STATUS.Due ? 'Due' : 'Active';
}

// Mirrors DepositInterestMath.MaturityDate for the form's read-only preview (the server stores the
// authoritative one): Days adds calendar days, Months adds months clamped to the target month's end
// (2026-01-31 + 1 month = 2026-02-28). Works on local calendar dates, like shared/date-only.ts.
export function depositMaturityDate(
  startDate: Date,
  termLength: number,
  termUnit: DepositTermUnit,
): Date {
  const year = startDate.getFullYear();
  const month = startDate.getMonth();
  const day = startDate.getDate();

  if (Number(termUnit) === DEPOSIT_TERM_UNIT.Days) {
    return new Date(year, month, day + termLength);
  }

  const lastDayOfTargetMonth = new Date(year, month + termLength + 1, 0).getDate();
  return new Date(year, month + termLength, Math.min(day, lastDayOfTargetMonth));
}

export function formatPercent(value: number | string): string {
  return `${new Intl.NumberFormat('pl-PL', { maximumFractionDigits: 4 }).format(Number(value))} %`;
}
