import { Injectable } from '@angular/core';
import {
  MAT_NATIVE_DATE_FORMATS,
  NativeDateAdapter,
  type MatDateFormats,
} from '@angular/material/core';

const DOTTED_FORMAT = 'dd.mm.yyyy';
const DOTTED_DATE = /^(\d{1,2})\.(\d{1,2})\.(\d{4})$/;

export const RANGE_DATE_FORMATS: MatDateFormats = {
  parse: { dateInput: DOTTED_FORMAT },
  display: { ...MAT_NATIVE_DATE_FORMATS.display, dateInput: DOTTED_FORMAT },
};

export function formatDottedDate(date: Date): string {
  const day = String(date.getDate()).padStart(2, '0');
  const month = String(date.getMonth() + 1).padStart(2, '0');
  return `${day}.${month}.${date.getFullYear()}`;
}

@Injectable()
export class RangeDateAdapter extends NativeDateAdapter {
  override parse(value: unknown, parseFormat?: unknown): Date | null {
    if (typeof value !== 'string') {
      return super.parse(value, parseFormat);
    }
    const text = value.trim();
    if (text === '') {
      return null;
    }
    const match = DOTTED_DATE.exec(text);
    if (match === null) {
      return this.invalid();
    }
    const [day, month, year] = match.slice(1).map(Number);
    const date = new Date(year, month - 1, day);
    const isReal =
      date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day;
    return isReal ? date : this.invalid();
  }

  override format(date: Date, displayFormat: object): string {
    return (displayFormat as unknown) === DOTTED_FORMAT
      ? formatDottedDate(date)
      : super.format(date, displayFormat);
  }
}
