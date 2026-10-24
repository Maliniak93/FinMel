import { TRANSLATIONS } from '../../../testing/i18n';

function placeholders(value: string): string[] {
  return Array.from(value.matchAll(/\{\{\s*([\w.]+)\s*\}\}/g), (match) => match[1]).sort();
}

describe('translation files', () => {
  it('en and pl define the same non-empty keys', () => {
    const enKeys = Object.keys(TRANSLATIONS.en).sort();
    const plKeys = Object.keys(TRANSLATIONS.pl).sort();

    expect(enKeys.length).toBeGreaterThan(0);
    expect(
      plKeys.filter((key) => !enKeys.includes(key)),
      'keys only in pl.json',
    ).toEqual([]);
    expect(
      enKeys.filter((key) => !plKeys.includes(key)),
      'keys only in en.json',
    ).toEqual([]);

    for (const lang of ['en', 'pl'] as const) {
      const invalid = Object.entries(TRANSLATIONS[lang])
        .filter(([, value]) => typeof value !== 'string' || value.trim() === '')
        .map(([key]) => key);
      expect(invalid, `empty or non-string values in ${lang}.json`).toEqual([]);
    }
  });

  it('en and pl define every groups.field key', () => {
    const keys = ['groups.field.group', 'groups.field.defaultGroup', 'groups.field.none'];

    for (const lang of ['en', 'pl'] as const) {
      expect(
        keys.filter((key) => !(key in TRANSLATIONS[lang])),
        `missing in ${lang}.json`,
      ).toEqual([]);
    }
  });

  it('en and pl define the Majątek lokaty and obligacje list copy', () => {
    const copy: Record<string, { en: string; pl: string }> = {
      'deposits.status.maturity': { en: 'Matures {{ date }}', pl: 'Zapada {{ date }}' },
      'deposits.subtitle': {
        en: '{{ rate }}% · Matures {{ date }}',
        pl: '{{ rate }} % · Zapadalność {{ date }}',
      },
      'bonds.status.interestDue': { en: 'Interest to settle', pl: 'Odsetki do rozliczenia' },
    };

    for (const [key, values] of Object.entries(copy)) {
      expect(TRANSLATIONS.en[key], `en ${key}`).toBe(values.en);
      expect(TRANSLATIONS.pl[key], `pl ${key}`).toBe(values.pl);
    }
  });

  it('en and pl use the same interpolation parameters for every key', () => {
    const mismatched = Object.keys(TRANSLATIONS.en).filter((key) => {
      const en = TRANSLATIONS.en[key];
      const pl = TRANSLATIONS.pl[key];
      return (
        typeof en === 'string' &&
        typeof pl === 'string' &&
        placeholders(en).join(',') !== placeholders(pl).join(',')
      );
    });

    expect(mismatched).toEqual([]);
  });
});
