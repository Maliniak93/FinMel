import { DOCUMENT, Injectable, PendingTasks, computed, inject, signal } from '@angular/core';
import { DateAdapter } from '@angular/material/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

export type Language = 'en' | 'pl';

export const LANGUAGES: readonly Language[] = ['en', 'pl'];
export const DEFAULT_LANGUAGE: Language = 'en';
export const LANGUAGE_STORAGE_KEY = 'skarbiec-lang';

// One Intl locale per language, for numbers, money, percentages, dates and the Material DateAdapter.
const LOCALES: Record<Language, string> = { en: 'en-US', pl: 'pl-PL' };

// Module-level rather than on the service: the shared formatters (shared/format.ts) are plain
// functions called from templates and computed()s, outside any injection context. Reading this
// signal there is what makes a formatted value re-render on a switch.
const activeLanguage = signal<Language>(DEFAULT_LANGUAGE);
export const activeLocale = computed(() => LOCALES[activeLanguage()]);

export function isLanguage(value: unknown): value is Language {
  return LANGUAGES.includes(value as Language);
}

// The active UI language. A display preference, not a credential — localStorage is fine here, as
// for `skarbiec-theme` (ADR-005's localStorage ban covers auth tokens). Nothing is stored until the
// user picks a language; an unknown stored value falls back to English.
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly transloco = inject(TranslocoService);
  private readonly document = inject(DOCUMENT);
  private readonly pendingTasks = inject(PendingTasks);
  // Optional so a spec that renders no datepicker needs no DateAdapter.
  private readonly dateAdapter = inject(DateAdapter, { optional: true });

  readonly language = activeLanguage.asReadonly();
  readonly locale = activeLocale;

  // App initializer: applies the stored language and waits for its translations, so the first
  // route never renders a raw key.
  async restore(): Promise<void> {
    const stored = localStorage.getItem(LANGUAGE_STORAGE_KEY);
    await this.apply(isLanguage(stored) ? stored : DEFAULT_LANGUAGE);
  }

  async setLanguage(language: Language): Promise<void> {
    await this.apply(language);
    localStorage.setItem(LANGUAGE_STORAGE_KEY, language);
  }

  private async apply(language: Language): Promise<void> {
    // A pending task keeps the app "unstable" until the switch has landed, so anything awaiting
    // stability (whenStable in specs) sees the new language.
    const done = this.pendingTasks.add();
    try {
      await firstValueFrom(this.transloco.load(language));
      this.transloco.setActiveLang(language);
      activeLanguage.set(language);
      this.document.documentElement.lang = language;
      this.dateAdapter?.setLocale(LOCALES[language]);
    } finally {
      done();
    }
  }
}
