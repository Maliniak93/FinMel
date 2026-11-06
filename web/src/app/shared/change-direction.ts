import { roundsToZero } from './format';

export type ChangeDirection = 'positive' | 'negative' | 'neutral';

export function changeDirection(
  amount: number | null | undefined,
  percent: number | null | undefined,
  percentDigits: number,
): ChangeDirection {
  if (amount !== null && amount !== undefined) {
    if (roundsToZero(amount)) {
      return 'neutral';
    }
    return amount > 0 ? 'positive' : 'negative';
  }
  if (percent === null || percent === undefined) {
    return 'neutral';
  }
  const factor = 10 ** percentDigits;
  const rounded = Math.round(Math.abs(percent) * factor);
  if (rounded === 0) {
    return 'neutral';
  }
  return percent > 0 ? 'positive' : 'negative';
}
