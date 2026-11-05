import { changeDirection } from './change-direction';

describe('changeDirection', () => {
  it('reads the sign of the amount rounded to grosze and ignores the percent', () => {
    expect(changeDirection(28142.83, 642.2, 2)).toBe('positive');
    expect(changeDirection(-1200, -3.1, 2)).toBe('negative');
    expect(changeDirection(0, 0, 2)).toBe('neutral');
    expect(changeDirection(0.004, null, 2)).toBe('neutral');
    expect(changeDirection(0.005, null, 2)).toBe('positive');
  });

  it('reads the percent only when there is no amount', () => {
    expect(changeDirection(null, null, 2)).toBe('neutral');
    expect(changeDirection(null, 0.001, 2)).toBe('neutral');
    expect(changeDirection(null, 0.05, 1)).toBe('positive');
    expect(changeDirection(null, -0.05, 1)).toBe('negative');
  });
});
