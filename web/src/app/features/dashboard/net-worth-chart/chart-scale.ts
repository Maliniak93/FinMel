export type ChartRange = '1M' | '1Y' | 'YTD' | 'MAX';

const STEP_MANTISSAS = [1, 2, 2.5, 5];
const MAX_X_LABELS = 7;
const MAX_DAY_LABELS = 8;
const DAY_STEPS = [1, 2, 3, 5, 7, 10, 14];
const MONTHS_BEFORE_YEARLY = 24;

export function niceTicks(min: number, max: number, count: number): number[] {
  let low = min;
  let high = max;
  if (high - low <= 0) {
    const pad = Math.abs(low) * 0.05 || 1;
    low -= pad;
    high += pad;
  }

  const maxTicks = count + 1;
  const startExponent = Math.floor(Math.log10((high - low) / maxTicks)) - 1;

  for (let exponent = startExponent; ; exponent++) {
    for (const mantissa of STEP_MANTISSAS) {
      const step = mantissa * 10 ** exponent;
      const first = Math.floor(low / step);
      const last = Math.ceil(high / step);
      if (last - first + 1 <= maxTicks) {
        return Array.from({ length: last - first + 1 }, (_, i) => (first + i) * step);
      }
    }
  }
}

function thin<T>(items: T[], maxCount: number): T[] {
  const stride = Math.ceil(items.length / maxCount);
  return stride <= 1 ? items : items.filter((_, index) => index % stride === 0);
}

function monthStarts(first: Date, last: Date): Date[] {
  const ticks: Date[] = [];
  for (
    let cursor = new Date(first.getFullYear(), first.getMonth(), 1);
    cursor.getTime() <= last.getTime();
    cursor = new Date(cursor.getFullYear(), cursor.getMonth() + 1, 1)
  ) {
    if (cursor.getTime() >= first.getTime()) {
      ticks.push(cursor);
    }
  }
  return ticks;
}

function yearStarts(first: Date, last: Date): Date[] {
  const ticks: Date[] = [];
  for (let year = first.getFullYear(); year <= last.getFullYear(); year++) {
    const tick = new Date(year, 0, 1);
    if (tick.getTime() >= first.getTime() && tick.getTime() <= last.getTime()) {
      ticks.push(tick);
    }
  }
  return ticks;
}

function dayTicks(first: Date, last: Date): Date[] {
  const spanDays = Math.round((last.getTime() - first.getTime()) / 86_400_000);
  const step =
    DAY_STEPS.find((candidate) => Math.floor(spanDays / candidate) + 1 <= MAX_DAY_LABELS) ??
    DAY_STEPS[DAY_STEPS.length - 1];
  const ticks: Date[] = [];
  for (let day = 0; day <= spanDays; day += step) {
    ticks.push(new Date(first.getFullYear(), first.getMonth(), first.getDate() + day));
  }
  return ticks;
}

export type XTickUnit = 'day' | 'month' | 'year';

export function xTickUnit(range: ChartRange, first: Date, last: Date): XTickUnit {
  if (range === '1M') {
    return 'day';
  }
  const months =
    (last.getFullYear() - first.getFullYear()) * 12 + last.getMonth() - first.getMonth();
  return range === 'MAX' && months > MONTHS_BEFORE_YEARLY ? 'year' : 'month';
}

export function xTicks(range: ChartRange, first: Date, last: Date): Date[] {
  const unit = xTickUnit(range, first, last);
  if (unit === 'day') {
    return dayTicks(first, last);
  }
  const ticks = unit === 'year' ? yearStarts(first, last) : monthStarts(first, last);
  return ticks.length === 0 ? [first] : thin(ticks, MAX_X_LABELS);
}
