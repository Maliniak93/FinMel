import { fromDateOnly, toDateOnly } from './date-only';

// AC-10: the local-midnight DateOnly helpers live once, here, for both the asset and transaction
// dialogs.
describe('date-only', () => {
  it('round-trips a DateOnly string through fromDateOnly and toDateOnly', () => {
    expect(toDateOnly(fromDateOnly('2026-03-05'))).toBe('2026-03-05');
  });

  it('fromDateOnly builds local midnight, not UTC midnight', () => {
    expect(fromDateOnly('2020-06-15')).toEqual(new Date(2020, 5, 15));
  });

  it('toDateOnly zero-pads month and day from the local calendar date', () => {
    expect(toDateOnly(new Date(2026, 0, 9, 23, 59))).toBe('2026-01-09');
  });
});
