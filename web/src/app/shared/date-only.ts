// A C# `DateOnly` travels as a "YYYY-MM-DD" string. Both helpers work on the *local* calendar date:
// never `new Date(dateOnly)` (parses as UTC midnight and shifts a day back in any negative-UTC-offset
// timezone) and never slicing `toISOString()` (UTC again) — so the round-trip never drifts,
// whatever the user's offset.
export function toDateOnly(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

// Local midnight for the given calendar date — the exact inverse of toDateOnly().
export function fromDateOnly(dateOnly: string): Date {
  const [year, month, day] = dateOnly.split('-').map(Number);
  return new Date(year, month - 1, day);
}
