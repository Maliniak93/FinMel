import { TestBed } from '@angular/core/testing';

import { isTranslationIn, looksLikeTranslationKey } from '../../../testing/i18n';
import { LANGUAGE_STORAGE_KEY, LanguageService } from '../i18n/language';
import { provideI18nTesting } from '../i18n/testing';
import { readProblemDetails } from './problem-details';

const ENGLISH_FALLBACK = 'Something went wrong. Please try again.';

describe('readProblemDetails', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideI18nTesting()] });
  });

  afterEach(async () => {
    await TestBed.inject(LanguageService).setLanguage('en');
    localStorage.removeItem(LANGUAGE_STORAGE_KEY);
  });

  it('falls back to the English generic message by default', async () => {
    await TestBed.inject(LanguageService).setLanguage('en');

    expect(readProblemDetails(undefined).detail).toBe(ENGLISH_FALLBACK);
  });

  it('keeps the server detail and translates the fallback', async () => {
    await TestBed.inject(LanguageService).setLanguage('pl');

    expect(readProblemDetails({ status: 404, detail: 'Asset not found.' }).detail).toBe(
      'Asset not found.',
    );
    expect(readProblemDetails('Bad gateway').detail).toBe('Bad gateway');

    const fallback = readProblemDetails(undefined).detail;
    expect(fallback).not.toBe(ENGLISH_FALLBACK);
    expect(looksLikeTranslationKey(fallback)).toBe(false);
    expect(isTranslationIn('pl', fallback)).toBe(true);
  });
});
