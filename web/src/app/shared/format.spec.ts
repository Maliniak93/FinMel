import { TestBed } from '@angular/core/testing';

import { provideI18nTesting } from '../core/i18n/testing';
import { LANGUAGE_STORAGE_KEY, LanguageService } from '../core/i18n/language';
import {
  formatDate,
  formatMoney,
  formatMoneyCompact,
  formatPercent,
  formatQuantity,
} from './format';

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
        new Intl.NumberFormat(locale, { style: 'currency', currency: 'PLN' }).format(1234.56),
      );
      expect(formatMoney('1234.56', 'USD')).toBe(
        new Intl.NumberFormat(locale, { style: 'currency', currency: 'USD' }).format(1234.56),
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
    expect(formatMoney(1234.56)).toBe(
      new Intl.NumberFormat('en-US', { style: 'currency', currency: 'PLN' }).format(1234.56),
    );
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
