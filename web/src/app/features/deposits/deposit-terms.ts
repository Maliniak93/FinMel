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

export const DEPOSIT_STATUS = { Active: 0, Due: 1, Settled: 2 } as const satisfies Record<
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
  switch (Number(status)) {
    case DEPOSIT_STATUS.Settled:
      return 'Settled';
    case DEPOSIT_STATUS.Due:
      return 'Due';
    default:
      return 'Active';
  }
}

// A settlement's net interest (gross − tax) and final amount (principal + net), for the settle
// dialog's live preview and a Settled row — the server stores only the three inputs. Summed in whole
// grosze so the display never shows a floating-point artefact; the server's decimals stay the truth.
export function settlementAmounts(
  principal: number | string,
  grossInterest: number | string,
  tax: number | string,
): { netInterest: number; finalAmount: number } {
  const toGrosze = (amount: number | string) => Math.round(Number(amount) * 100);
  const netGrosze = toGrosze(grossInterest) - toGrosze(tax);
  return {
    netInterest: netGrosze / 100,
    finalAmount: (toGrosze(principal) + netGrosze) / 100,
  };
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
