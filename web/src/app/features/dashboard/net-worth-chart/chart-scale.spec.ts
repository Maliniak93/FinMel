import { fromDateOnly } from '../../../shared/date-only';
import { niceTicks, xTicks } from './chart-scale';

function isRoundStep(step: number): boolean {
  const magnitude = 10 ** Math.floor(Math.log10(step));
  const mantissa = step / magnitude;
  return [1, 2, 2.5, 5, 10].some((allowed) => Math.abs(mantissa - allowed) < 1e-9);
}

describe('niceTicks', () => {
  it('returns evenly spaced round values bracketing both ends', () => {
    const ticks = niceTicks(213_400, 298_450, 5);

    expect(ticks.length).toBeGreaterThanOrEqual(4);
    expect(ticks.length).toBeLessThanOrEqual(6);
    expect(ticks[0]).toBeLessThanOrEqual(213_400);
    expect(ticks[ticks.length - 1]).toBeGreaterThanOrEqual(298_450);

    const step = ticks[1] - ticks[0];
    expect(isRoundStep(step)).toBe(true);
    for (let i = 1; i < ticks.length; i++) {
      expect(ticks[i] - ticks[i - 1]).toBeCloseTo(step, 6);
    }
  });

  it('still returns at least two ticks when min equals max', () => {
    const ticks = niceTicks(5000, 5000, 5);

    expect(ticks.length).toBeGreaterThanOrEqual(2);
    expect(ticks[0]).toBeLessThanOrEqual(5000);
    expect(ticks[ticks.length - 1]).toBeGreaterThanOrEqual(5000);
  });
});

describe('x ticks per range', () => {
  it('puts 1Y ticks on the first day of each month, starting at local midnight', () => {
    const first = fromDateOnly('2026-01-01');
    const last = fromDateOnly('2026-12-31');

    const ticks = xTicks('1Y', first, last);

    expect(ticks.length).toBeGreaterThanOrEqual(2);
    expect(ticks[0].getTime()).toBe(new Date(2026, 0, 1).getTime());
    for (const tick of ticks) {
      expect(tick.getDate()).toBe(1);
      expect(tick.getTime()).toBeGreaterThanOrEqual(first.getTime());
      expect(tick.getTime()).toBeLessThanOrEqual(last.getTime());
    }
  });

  it('puts 1M ticks on days', () => {
    const first = fromDateOnly('2026-08-01');
    const last = fromDateOnly('2026-08-31');

    const ticks = xTicks('1M', first, last);

    expect(ticks.length).toBeGreaterThanOrEqual(4);
    expect(ticks.length).toBeLessThanOrEqual(31);
    expect(ticks[0].getTime()).toBe(new Date(2026, 7, 1).getTime());
    for (const tick of ticks) {
      expect(tick.getHours()).toBe(0);
      expect(tick.getMonth()).toBe(7);
      expect(tick.getTime()).toBeGreaterThanOrEqual(first.getTime());
      expect(tick.getTime()).toBeLessThanOrEqual(last.getTime());
    }
  });

  it('puts MAX ticks on years when the span exceeds 24 months', () => {
    const ticks = xTicks('MAX', fromDateOnly('2021-03-15'), fromDateOnly('2026-08-01'));

    expect(ticks.length).toBeGreaterThanOrEqual(2);
    for (const tick of ticks) {
      expect(tick.getMonth()).toBe(0);
      expect(tick.getDate()).toBe(1);
    }
  });

  it('puts MAX ticks on months when the span is 24 months or less', () => {
    const ticks = xTicks('MAX', fromDateOnly('2025-09-10'), fromDateOnly('2026-08-01'));

    expect(ticks.length).toBeGreaterThanOrEqual(2);
    expect(ticks.some((tick) => tick.getMonth() !== 0)).toBe(true);
    for (const tick of ticks) {
      expect(tick.getDate()).toBe(1);
    }
  });
});
