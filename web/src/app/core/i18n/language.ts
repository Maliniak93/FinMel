import { DOCUMENT, Injectable, PendingTasks, computed, inject, signal } from '@angular/core';
import { DateAdapter } from '@angular/material/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

export type Language = 'en' | 'pl';

export const LANGUAGES: readonly Language[] = ['en', 'pl'];
export const DEFAULT_LANGUAGE: Language = 'en';
export const LANGUAGE_STORAGE_KEY = 'skarbiec-lang';

const LOCALES: Record<Language, string> = { en: 'en-US', pl: 'pl-PL' };

// Module-level so the plain formatters in shared/format.ts can read it outside an injection context.
const activeLanguage = signal<Language>(DEFAULT_LANGUAGE);
export const activeLocale = computed(() => LOCALES[activeLanguage()]);

export function isLanguage(value: unknown): value is Language {
  return LANGUAGES.includes(value as Language);
}

@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly transloco = inject(TranslocoService);
  private readonly document = inject(DOCUMENT);
  private readonly pendingTasks = inject(PendingTasks);
  private readonly dateAdapter = inject(DateAdapter, { optional: true });

  readonly language = activeLanguage.asReadonly();
  readonly locale = activeLocale;

  async restore(): Promise<void> {
    const stored = localStorage.getItem(LANGUAGE_STORAGE_KEY);
    await this.apply(isLanguage(stored) ? stored : DEFAULT_LANGUAGE);
  }

  async setLanguage(language: Language): Promise<void> {
    await this.apply(language);
    localStorage.setItem(LANGUAGE_STORAGE_KEY, language);
  }

  private async apply(language: Language): Promise<void> {
    // The pending task keeps the app unstable until the switch lands, so whenStable() sees the new language.
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
