import { ApplicationInitStatus } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DateAdapter, provideNativeDateAdapter } from '@angular/material/core';
import { TranslocoService } from '@jsverse/transloco';

import { TRANSLATIONS } from '../../../testing/i18n';
import { provideI18n } from './i18n';
import { LANGUAGE_STORAGE_KEY, LanguageService } from './language';

// i18n foundation (#131) AC-1/2/3/8. Boots the real i18n providers (Transloco config, the dynamic
// `import()` loader and the app initializer that restores the stored language) the way
// app.config.ts does, then waits for the app initializers exactly as the first route would.
async function startApp(): Promise<void> {
  TestBed.configureTestingModule({
    providers: [provideI18n(), provideNativeDateAdapter()],
  });
  await TestBed.inject(ApplicationInitStatus).donePromise;
}

describe('LanguageService', () => {
  beforeEach(() => {
    localStorage.removeItem(LANGUAGE_STORAGE_KEY);
    // Something other than what the app should write, so the assertions prove it wrote it.
    document.documentElement.lang = 'xx';
  });

  afterEach(async () => {
    // Specs share one worker (isolate: false) — never leave Polish active for the next file.
    await TestBed.inject(LanguageService).setLanguage('en');
    localStorage.removeItem(LANGUAGE_STORAGE_KEY);
    document.documentElement.lang = 'en';
  });

  // AC-1
  it('defaults to English when nothing is stored', async () => {
    await startApp();

    const language = TestBed.inject(LanguageService);
    const transloco = TestBed.inject(TranslocoService);
    expect(language.language()).toBe('en');
    expect(language.locale()).toBe('en-US');
    expect(transloco.getActiveLang()).toBe('en');
    expect(document.documentElement.lang).toBe('en');
    expect(Object.keys(transloco.getTranslation('en')).length).toBeGreaterThan(0);
    // Design decision: nothing is written until the user picks a language.
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBeNull();
  });

  // AC-2
  it('falls back to English for an unknown stored value', async () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'xx');

    await startApp();

    const language = TestBed.inject(LanguageService);
    expect(language.language()).toBe('en');
    expect(language.locale()).toBe('en-US');
    expect(TestBed.inject(TranslocoService).getActiveLang()).toBe('en');
    expect(document.documentElement.lang).toBe('en');
  });

  // AC-3
  it('restores the stored language before first render', async () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'pl');

    await startApp();

    const language = TestBed.inject(LanguageService);
    const transloco = TestBed.inject(TranslocoService);
    expect(language.language()).toBe('pl');
    expect(language.locale()).toBe('pl-PL');
    expect(transloco.getActiveLang()).toBe('pl');
    expect(document.documentElement.lang).toBe('pl');

    // Loaded before the initializer resolved: a synchronous translate already yields Polish, so
    // the first route never flashes a raw key.
    const [key, polish] = Object.entries(TRANSLATIONS.pl)[0];
    expect(transloco.translate(key)).toBe(polish);
  });

  // AC-8
  it('sets the date adapter locale on switch', async () => {
    await startApp();
    const setLocale = vi.spyOn(TestBed.inject(DateAdapter), 'setLocale');
    const language = TestBed.inject(LanguageService);

    await language.setLanguage('pl');
    expect(setLocale).toHaveBeenLastCalledWith('pl-PL');

    await language.setLanguage('en');
    expect(setLocale).toHaveBeenLastCalledWith('en-US');
  });

  it('switching persists the choice, updates <html lang> and the active locale', async () => {
    await startApp();
    const language = TestBed.inject(LanguageService);

    await language.setLanguage('pl');

    expect(language.language()).toBe('pl');
    expect(language.locale()).toBe('pl-PL');
    expect(TestBed.inject(TranslocoService).getActiveLang()).toBe('pl');
    expect(document.documentElement.lang).toBe('pl');
    expect(localStorage.getItem('skarbiec-lang')).toBe('pl');
  });
});
