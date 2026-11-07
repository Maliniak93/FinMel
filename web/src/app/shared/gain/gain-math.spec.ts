import { gainOf, sumGains } from './gain-math';

describe('gain math', () => {
  it('computes the amount and its percent exactly to the grosz', () => {
    expect(gainOf({ value: 3647.7, cost: 3500 })).toMatchObject({ amount: 147.7, percent: 4.22 });
    expect(gainOf({ value: 1000, cost: 1000, paid: 65 })).toMatchObject({
      amount: 65,
      percent: 6.5,
    });
    expect(gainOf({ value: 1, cost: 3 })).toMatchObject({ amount: -2, percent: -66.67 });
  });

  it('gives no gain for a zero cost or a missing value or cost', () => {
    expect(gainOf({ value: 100, cost: 0 })).toBeNull();
    expect(gainOf({ value: null, cost: 100 })).toBeNull();
    expect(gainOf({ value: 100, cost: null })).toBeNull();
  });

  it('sums the gains and the costs of the rows that have a gain only', () => {
    const total = sumGains([
      { value: 3647.7, cost: 3500 },
      { value: 1000, cost: 1000, paid: 65 },
      { value: null, cost: 500 },
      { value: 500, cost: 0 },
    ]);

    expect(total).toMatchObject({ amount: 212.7, cost: 4500, percent: 4.73, withoutGain: 2 });
  });
});
