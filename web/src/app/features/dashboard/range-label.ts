import { translate } from '@jsverse/transloco';

import { fromDateOnly } from '../../shared/date-only';
import { formatDottedDate } from './range-dialog/range-date';

export interface CustomRange {
  readonly from: string;
  readonly to: string;
}

function startOfToday(): Date {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

export function customRangeLabel(range: CustomRange): string {
  const from = fromDateOnly(range.from);
  const to = fromDateOnly(range.to);
  const today = startOfToday();
  const year = from.getFullYear();
  const startsJanuaryFirst = from.getMonth() === 0 && from.getDate() === 1;

  if (
    startsJanuaryFirst &&
    year === today.getFullYear() &&
    to.getFullYear() === year &&
    to >= today
  ) {
    return translate('dashboard.range.yearToDate', { year });
  }
  if (
    startsJanuaryFirst &&
    to.getFullYear() === year &&
    to.getMonth() === 11 &&
    to.getDate() === 31
  ) {
    return String(year);
  }
  return `${formatDottedDate(from)}–${formatDottedDate(to)}`;
}
