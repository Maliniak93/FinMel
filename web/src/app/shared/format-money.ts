// Server computes all monetary math (angular.md) — this only formats. Shared because dashboard,
// the net-worth chart, and the assets list all need the identical PLN/instrument-currency display.
export function formatMoney(amount: number | string, currency = 'PLN'): string {
  try {
    return new Intl.NumberFormat('pl-PL', { style: 'currency', currency }).format(Number(amount));
  } catch {
    return `${amount} ${currency}`;
  }
}
