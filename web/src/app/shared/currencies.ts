// Mirrors Skarbiec.Contracts.SupportedCurrencies — the codes a user may pick for a portfolio or an
// asset. The set doesn't reach the generated client (Currency is a plain string in the OpenAPI
// schema), so it is maintained here, in the same order as the C# list, and nowhere else: dialogs
// import this instead of keeping their own copy. A label is a translation key: templates render it
// through the `transloco` pipe.
//
// Unrelated to formatMoney()'s 'PLN' default — that one is the base currency amounts are reported
// in (ADR-008), not a set the user chooses from.
export const SUPPORTED_CURRENCIES: readonly { code: string; label: string }[] = [
  { code: 'PLN', label: 'enums.currency.pln' },
  { code: 'EUR', label: 'enums.currency.eur' },
  { code: 'USD', label: 'enums.currency.usd' },
];

export const DEFAULT_CURRENCY = 'PLN';
