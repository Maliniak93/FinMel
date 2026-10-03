// Mirrors Skarbiec.Contracts.SupportedCurrencies in order; the OpenAPI schema types Currency as a plain string.
export const SUPPORTED_CURRENCIES: readonly { code: string; label: string }[] = [
  { code: 'PLN', label: 'enums.currency.pln' },
  { code: 'EUR', label: 'enums.currency.eur' },
  { code: 'USD', label: 'enums.currency.usd' },
];

export const DEFAULT_CURRENCY = 'PLN';
