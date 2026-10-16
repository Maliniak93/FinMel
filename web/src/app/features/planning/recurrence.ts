import type { BusinessDayShift, RecurrenceDayKind } from '../../api/portfolio';

// Mirrors Skarbiec.Portfolio.Features.Planning.Recurrence: the enums travel as their ints.
export const DAY_KIND = { DayOfMonth: 0, LastBusinessDay: 1 } as const satisfies Record<
  string,
  RecurrenceDayKind
>;

export const SHIFT = {
  None: 0,
  PreviousBusinessDay: 1,
  NextBusinessDay: 2,
} as const satisfies Record<string, BusinessDayShift>;

export const SHIFT_OPTIONS = [
  { value: SHIFT.None, label: 'planning.shift.none' },
  { value: SHIFT.PreviousBusinessDay, label: 'planning.shift.previous' },
  { value: SHIFT.NextBusinessDay, label: 'planning.shift.next' },
] as const;

export function currentWarsawMonth(): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Europe/Warsaw',
    year: 'numeric',
    month: '2-digit',
  }).formatToParts(new Date());
  const year = parts.find((part) => part.type === 'year')!.value;
  const month = parts.find((part) => part.type === 'month')!.value;
  return `${year}-${month}`;
}

export function currentWarsawDate(): Date {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Europe/Warsaw',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(new Date());
  const value = (type: string): number => Number(parts.find((part) => part.type === type)!.value);
  return new Date(value('year'), value('month') - 1, value('day'));
}

export interface RulePart {
  key: string;
  params?: Record<string, number>;
}

export function ruleParts(rule: {
  intervalMonths: number | string;
  dayKind: number;
  dayOfMonth?: number | string | null;
  shift: number;
}): RulePart[] {
  const interval = Number(rule.intervalMonths);
  const parts: RulePart[] = [
    interval === 1
      ? { key: 'planning.rule.monthly' }
      : { key: 'planning.rule.everyMonths', params: { count: interval } },
  ];

  if (rule.dayKind === DAY_KIND.LastBusinessDay) {
    parts.push({ key: 'planning.rule.lastBusinessDay' });
    return parts;
  }

  parts.push({ key: 'planning.rule.dayOfMonth', params: { day: Number(rule.dayOfMonth) } });
  if (rule.shift === SHIFT.PreviousBusinessDay) {
    parts.push({ key: 'planning.rule.previousBusinessDay' });
  } else if (rule.shift === SHIFT.NextBusinessDay) {
    parts.push({ key: 'planning.rule.nextBusinessDay' });
  }
  return parts;
}
