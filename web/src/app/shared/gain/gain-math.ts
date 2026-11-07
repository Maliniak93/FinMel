export interface GainInput {
  value: number | string | null | undefined;
  cost: number | string | null | undefined;
  paid?: number | string | null;
}

export interface Gain {
  amount: number;
  percent: number;
}

export interface GainTotal extends Gain {
  cost: number;
  withoutGain: number;
}

function grosze(amount: number | string): number {
  return Math.round(Number(amount) * 100);
}

function percentOf(amountGrosze: number, costGrosze: number): number {
  return Math.round((amountGrosze * 10000) / costGrosze) / 100;
}

// Display-only gain in whole grosze; the percent is rounded to 2 decimals.
export function gainOf(input: GainInput): Gain | null {
  if (input.value === null || input.value === undefined) {
    return null;
  }
  if (input.cost === null || input.cost === undefined) {
    return null;
  }
  const cost = grosze(input.cost);
  if (cost <= 0) {
    return null;
  }
  const amount = grosze(input.value) + grosze(input.paid ?? 0) - cost;
  return { amount: amount / 100, percent: percentOf(amount, cost) };
}

export function sumGains(rows: readonly GainInput[]): GainTotal | null {
  let amount = 0;
  let cost = 0;
  let withGain = 0;
  for (const row of rows) {
    const gain = gainOf(row);
    if (gain) {
      amount += grosze(gain.amount);
      cost += grosze(row.cost ?? 0);
      withGain++;
    }
  }
  return withGain === 0
    ? null
    : {
        amount: amount / 100,
        cost: cost / 100,
        percent: percentOf(amount, cost),
        withoutGain: rows.length - withGain,
      };
}
