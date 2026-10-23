import { TestBed } from '@angular/core/testing';

import { provideI18nTesting } from '../core/i18n/testing';
import { LANGUAGE_STORAGE_KEY, LanguageService } from '../core/i18n/language';
import {
  formatDate,
  formatMoney,
  formatMoneyCompact,
  formatPercent,
  formatQuantity,
  formatSignedMoney,
} from './format';

function flat(text: string): string {
  return text.replace(/\s/g, ' ');
}

const LOCALES = { en: 'en-US', pl: 'pl-PL' } as const;

describe('locale-aware formatters', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideI18nTesting()] });
  });

  afterEach(async () => {
    await TestBed.inject(LanguageService).setLanguage('en');
    localStorage.removeItem(LANGUAGE_STORAGE_KEY);
  });

  it('formats money, quantities, percentages and dates for the active language', async () => {
    const language = TestBed.inject(LanguageService);

    for (const lang of ['en', 'pl', 'en'] as const) {
      await language.setLanguage(lang);
      const locale = LOCALES[lang];

      expect(formatMoney(1234.56, 'PLN')).toBe(
        new Intl.NumberFormat(locale, {
          style: 'currency',
          currency: 'PLN',
          useGrouping: 'always',
        } as unknown as Intl.NumberFormatOptions).format(1234.56),
      );
      expect(formatMoney('1234.56', 'USD')).toBe(
        new Intl.NumberFormat(locale, {
          style: 'currency',
          currency: 'USD',
          currencyDisplay: 'code',
          useGrouping: 'always',
        } as unknown as Intl.NumberFormatOptions).format(1234.56),
      );
      const percent = formatPercent(5.25);
      expect(percent).toContain(
        new Intl.NumberFormat(locale, { maximumFractionDigits: 4 }).format(5.25),
      );
      expect(percent).toContain('%');
    }

    await language.setLanguage('en');
    expect(formatDate(new Date(2026, 8, 27))).toBe('Sep 27, 2026');
    expect(formatDate('2026-09-27')).toBe('Sep 27, 2026');

    await language.setLanguage('pl');
    expect(formatDate(new Date(2026, 8, 27))).toBe('27 wrz 2026');
    expect(formatDate('2026-09-27')).toBe('27 wrz 2026');
  });

  it('formatMoneyCompact', async () => {
    const language = TestBed.inject(LanguageService);

    for (const lang of ['en', 'pl', 'en'] as const) {
      await language.setLanguage(lang);
      const locale = LOCALES[lang];

      expect(formatMoneyCompact(250000)).toBe(
        new Intl.NumberFormat(locale, {
          style: 'currency',
          currency: 'PLN',
          notation: 'compact',
        }).format(250000),
      );
      expect(formatMoneyCompact(250000, 'USD')).toBe(
        new Intl.NumberFormat(locale, {
          style: 'currency',
          currency: 'USD',
          notation: 'compact',
        }).format(250000),
      );
    }
  });

  it('formats money in the default language (English) before any switch', () => {
    expect(flat(formatMoney(1234.56))).toBe('PLN 1,234.56');
  });

  it('formats money for the new rules', async () => {
    const language = TestBed.inject(LanguageService);

    await language.setLanguage('pl');
    expect(flat(formatMoney(12345.67, 'PLN'))).toBe('12 345,67 zł');
    expect(flat(formatMoney(420, 'EUR'))).toBe('420,00 EUR');
    expect(formatMoney(420, 'EUR')).not.toContain('€');

    await language.setLanguage('en');
    expect(flat(formatMoney(12345.67, 'PLN'))).toBe('PLN 12,345.67');
    expect(flat(formatMoney(420, 'EUR'))).toBe('EUR 420.00');
  });

  it('negative amounts use the true minus', async () => {
    const language = TestBed.inject(LanguageService);

    await language.setLanguage('pl');
    expect(flat(formatMoney(-1220, 'PLN'))).toBe('−1 220,00 zł');
    expect(formatMoney(-5, 'EUR')).toContain('−');
    expect(formatMoney(-5, 'EUR')).not.toContain('-');
    expect(formatMoneyCompact(-250000)).toContain('−');
    expect(formatMoneyCompact(-250000)).not.toContain('-');

    await language.setLanguage('en');
    expect(formatMoney(-1220, 'PLN')).toContain('−');
    expect(formatMoney(-1220, 'PLN')).not.toContain('-');
  });

  it('zero has no sign', async () => {
    await TestBed.inject(LanguageService).setLanguage('pl');

    for (const value of [0, -0, -0.001, 0.004]) {
      expect(flat(formatMoney(value, 'PLN'))).toBe('0,00 zł');
    }
  });

  it('signed money', async () => {
    await TestBed.inject(LanguageService).setLanguage('pl');

    expect(flat(formatSignedMoney(41208.1, 'PLN'))).toBe('+41 208,10 zł');
    expect(flat(formatSignedMoney(-1220, 'PLN'))).toBe('−1 220,00 zł');
    expect(flat(formatSignedMoney(0, 'PLN'))).toBe('0,00 zł');
    expect(flat(formatSignedMoney(0.004, 'PLN'))).toBe('0,00 zł');
    expect(flat(formatSignedMoney(-0.004, 'PLN'))).toBe('0,00 zł');
  });

  it('formats a quantity with at most two decimals, rounded half-up, grouped, without trailing zeros', async () => {
    const language = TestBed.inject(LanguageService);
    const cases = [
      [31.1034768, '31.1', '31,1'],
      [10, '10', '10'],
      [0.125, '0.13', '0,13'],
      [1234.5, '1,234.5', '1 234,5'],
      [1.005, '1.01', '1,01'],
      [12345.123456, '12,345.12', '12 345,12'],
    ] as const;

    for (const lang of ['en', 'pl', 'en'] as const) {
      await language.setLanguage(lang);
      for (const [value, en, pl] of cases) {
        expect(formatQuantity(value)).toBe(lang === 'en' ? en : pl);
      }
    }
  });

  it('formats a non-zero quantity that rounds to zero as a marker, and an exact zero as 0', async () => {
    const language = TestBed.inject(LanguageService);

    for (const lang of ['en', 'pl', 'en'] as const) {
      await language.setLanguage(lang);

      expect(formatQuantity(0.004)).toBe(lang === 'en' ? '< 0.01' : '< 0,01');
      expect(formatQuantity(-0.004)).toBe(lang === 'en' ? '> -0.01' : '> -0,01');
      expect(formatQuantity(0)).toBe('0');
    }
  });
});
