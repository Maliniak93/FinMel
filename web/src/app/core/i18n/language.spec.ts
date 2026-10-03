import { ApplicationInitStatus } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DateAdapter, provideNativeDateAdapter } from '@angular/material/core';
import { TranslocoService } from '@jsverse/transloco';

import { TRANSLATIONS } from '../../../testing/i18n';
import { provideI18n } from './i18n';
import { LANGUAGE_STORAGE_KEY, LanguageService } from './language';

async function startApp(): Promise<void> {
  TestBed.configureTestingModule({
    providers: [provideI18n(), provideNativeDateAdapter()],
  });
  await TestBed.inject(ApplicationInitStatus).donePromise;
}

describe('LanguageService', () => {
  beforeEach(() => {
    localStorage.removeItem(LANGUAGE_STORAGE_KEY);
    document.documentElement.lang = 'xx';
  });

  afterEach(async () => {
    await TestBed.inject(LanguageService).setLanguage('en');
    localStorage.removeItem(LANGUAGE_STORAGE_KEY);
    document.documentElement.lang = 'en';
  });

  it('defaults to English when nothing is stored', async () => {
    await startApp();

    const language = TestBed.inject(LanguageService);
    const transloco = TestBed.inject(TranslocoService);
    expect(language.language()).toBe('en');
    expect(language.locale()).toBe('en-US');
    expect(transloco.getActiveLang()).toBe('en');
    expect(document.documentElement.lang).toBe('en');
    expect(Object.keys(transloco.getTranslation('en')).length).toBeGreaterThan(0);
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBeNull();
  });

  it('falls back to English for an unknown stored value', async () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'xx');

    await startApp();

    const language = TestBed.inject(LanguageService);
    expect(language.language()).toBe('en');
    expect(language.locale()).toBe('en-US');
    expect(TestBed.inject(TranslocoService).getActiveLang()).toBe('en');
    expect(document.documentElement.lang).toBe('en');
  });

  it('restores the stored language before first render', async () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'pl');

    await startApp();

    const language = TestBed.inject(LanguageService);
    const transloco = TestBed.inject(TranslocoService);
    expect(language.language()).toBe('pl');
    expect(language.locale()).toBe('pl-PL');
    expect(transloco.getActiveLang()).toBe('pl');
    expect(document.documentElement.lang).toBe('pl');

    const [key, polish] = Object.entries(TRANSLATIONS.pl)[0];
    expect(transloco.translate(key)).toBe(polish);
  });

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
