import { DEFAULT_CURRENCY, SUPPORTED_CURRENCIES } from './currencies';

describe('supported currencies', () => {
  it('holds exactly the codes Skarbiec.Contracts.SupportedCurrencies accepts, in the same order', () => {
    expect(SUPPORTED_CURRENCIES.map((c) => c.code)).toEqual(['PLN', 'EUR', 'USD']);
  });

  it('defaults to PLN, and the default is a member of the set', () => {
    expect(DEFAULT_CURRENCY).toBe('PLN');
    expect(SUPPORTED_CURRENCIES.some((c) => c.code === DEFAULT_CURRENCY)).toBe(true);
  });

  it('labels every code and starts each label with the code the server stores', () => {
    for (const { code, label } of SUPPORTED_CURRENCIES) {
      expect(code).toMatch(/^[A-Z]{3}$/);
      expect(label.startsWith(code)).toBe(true);
    }
  });
});
