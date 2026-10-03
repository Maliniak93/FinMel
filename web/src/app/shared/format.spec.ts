import { TestBed } from '@angular/core/testing';

import { provideI18nTesting } from '../core/i18n/testing';
import { LANGUAGE_STORAGE_KEY, LanguageService } from '../core/i18n/language';
import { formatDate, formatMoney, formatPercent, formatQuantity } from './format';

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
      expect(formatQuantity(12345.123456)).toBe(
        new Intl.NumberFormat(locale, { maximumFractionDigits: 8 }).format(12345.123456),
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

  it('formats money in the default language (English) before any switch', () => {
    expect(formatMoney(1234.56)).toBe(
      new Intl.NumberFormat('en-US', { style: 'currency', currency: 'PLN' }).format(1234.56),
    );
  });
});
