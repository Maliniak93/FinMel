import type { DepositCapitalization, DepositStatus, DepositTermUnit } from '../../api/portfolio';

// Mirrors DepositTermUnit, DepositCapitalization and DepositStatus in declaration order: the enums travel as ints.
export const DEPOSIT_TERM_UNIT = { Days: 0, Months: 1 } as const satisfies Record<
  string,
  DepositTermUnit
>;

export const DEPOSIT_TERM_UNITS: readonly { value: DepositTermUnit; label: string }[] = [
  { value: DEPOSIT_TERM_UNIT.Days, label: 'enums.depositTermUnit.days' },
  { value: DEPOSIT_TERM_UNIT.Months, label: 'enums.depositTermUnit.months' },
];

export const DEPOSIT_CAPITALIZATIONS: readonly { value: DepositCapitalization; label: string }[] = [
  { value: 0, label: 'enums.depositCapitalization.atMaturity' },
  { value: 1, label: 'enums.depositCapitalization.monthly' },
  { value: 2, label: 'enums.depositCapitalization.quarterly' },
  { value: 3, label: 'enums.depositCapitalization.yearly' },
];

export const DEPOSIT_STATUS = {
  Active: 0,
  Due: 1,
  Settled: 2,
  PaidOut: 3,
} as const satisfies Record<string, DepositStatus>;

export const MAX_TERM_DAYS = 3650;
export const MAX_TERM_MONTHS = 120;

export function maxTermLength(termUnit: DepositTermUnit): number {
  return Number(termUnit) === DEPOSIT_TERM_UNIT.Days ? MAX_TERM_DAYS : MAX_TERM_MONTHS;
}

export function depositStatusLabel(status: DepositStatus): string {
  switch (Number(status)) {
    case DEPOSIT_STATUS.PaidOut:
      return 'enums.depositStatus.paidOut';
    case DEPOSIT_STATUS.Settled:
      return 'enums.depositStatus.settled';
    case DEPOSIT_STATUS.Due:
      return 'enums.depositStatus.due';
    default:
      return 'enums.depositStatus.active';
  }
}

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

// Mirrors DepositInterestMath.MaturityDate; Months clamps to the target month's end.
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
